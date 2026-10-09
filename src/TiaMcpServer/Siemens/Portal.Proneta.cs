using Microsoft.Extensions.Logging;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region network topology comparison (PRONETA CSV)

        // Compares a PRONETA "Device Table (CSV)" export (Network Analysis > Online > Export,
        // with "Include device details" checked - that's what adds the Port ID/Partner Port ID/
        // Partner Device Name columns this needs) against the currently open project's devices.
        // Read-only / report only - matching is by case-insensitive device name (PROFINET device
        // names are lowercase by spec, so e.g. project device "PLC_1" vs PRONETA's discovered
        // "plc_1" is the expected normal case, not a mismatch). MAC-address matching was
        // considered first but isn't possible: verified via the shipped Openness XML docs that no
        // MAC-address attribute/property exists anywhere in this API surface, on Node or
        // otherwise - so name is the only available matching key, and is therefore best-effort,
        // not guaranteed-correct. No write happens here; use SetIpAddress separately once a
        // mismatch is confirmed by a human.
        public List<NetworkImportMatch> CompareNetworkCsv(string csvPath)
        {
            _logger?.LogInformation($"Comparing PRONETA CSV: {csvPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var pronetaDevices = ParsePronetaCsv(csvPath);
            var tiaDevices = GetDevices();
            var results = new List<NetworkImportMatch>();

            foreach (var pd in pronetaDevices)
            {
                var match = new NetworkImportMatch { PronetaDevice = pd };

                var nameMatch = tiaDevices.FirstOrDefault(d => d.Name.Equals(pd.Name, StringComparison.OrdinalIgnoreCase));
                if (nameMatch != null)
                {
                    match.MatchedTiaDeviceName = nameMatch.Name;
                    match.MatchMethod = "name";

                    // Don't go through ResolveNetworkInterface here - a device can have more than
                    // one interface (e.g. an HMI's two communication processors), which is
                    // "ambiguous" from a single-path lookup but not actually a problem here: walk
                    // every interface under the device and prefer whichever node's IP matches the
                    // PRONETA row, falling back to the first node found if none match exactly.
                    var candidates = new List<(DeviceItem Item, string Path)>();
                    if (nameMatch.DeviceItems != null)
                    {
                        foreach (var child in nameMatch.DeviceItems)
                        {
                            CollectNetworkInterfaceDeviceItems(child, nameMatch.Name, candidates);
                        }
                    }

                    foreach (var (ifaceItem, _) in candidates)
                    {
                        var iface = ifaceItem.GetService<NetworkInterface>();
                        if (iface == null)
                        {
                            continue;
                        }

                        foreach (var node in iface.Nodes)
                        {
                            // Not every Node supports "Address"/"SubnetMask" (e.g. a PROFIBUS
                            // node has neither) - GetAttribute throws for an unsupported name on
                            // this instance, so treat that as "no IP info from this node" rather
                            // than failing the whole comparison.
                            string? addr = null;
                            string? mask = null;
                            try
                            {
                                addr = node.GetAttribute("Address") as string;
                                mask = node.GetAttribute("SubnetMask") as string;
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            var pronetaIp = pd.IpAddress;

                            var isExactMatch = !string.IsNullOrEmpty(pronetaIp) && !string.IsNullOrEmpty(addr)
                                && pronetaIp!.Equals(addr, StringComparison.OrdinalIgnoreCase);

                            if (isExactMatch || match.TiaCurrentIp == null)
                            {
                                match.TiaCurrentIp = addr;
                                match.TiaCurrentSubnetMask = mask;
                            }

                            if (isExactMatch)
                            {
                                break;
                            }
                        }
                    }

                    var pronetaIpToCompare = pd.IpAddress;
                    var tiaIpToCompare = match.TiaCurrentIp;
                    if (!string.IsNullOrEmpty(pronetaIpToCompare) && !string.IsNullOrEmpty(tiaIpToCompare))
                    {
                        match.IpDiffers = !pronetaIpToCompare!.Equals(tiaIpToCompare, StringComparison.OrdinalIgnoreCase);
                    }
                }

                results.Add(match);
            }

            return results;
        }

        public List<PronetaDevice> ParsePronetaCsv(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                throw new PortalException(PortalErrorCode.NotFound, $"CSV file not found at '{csvPath}'");
            }

            var lines = File.ReadAllLines(csvPath);
            var devices = new List<PronetaDevice>();
            PronetaDevice? current = null;

            // Find the real header row (contains "Device Type") - skips "sep=," and the
            // "Online Topology" title row that precede it.
            var startIndex = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Device Type"))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            for (var i = startIndex; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
                {
                    current = null; // blank row = device separator
                    continue;
                }

                // A new device is signaled by the leading "#" (device index) column, not by
                // Name - PRONETA can discover a device with no configured name at all (e.g. an
                // RF1100 reader straight from the factory), and that's still a real device that
                // needs to show up in the report, not silently vanish because Name was blank.
                var deviceIndex = fields.ElementAtOrDefault(0);
                if (!string.IsNullOrWhiteSpace(deviceIndex))
                {
                    current = new PronetaDevice
                    {
                        Name = fields.ElementAtOrDefault(1) ?? "",
                        DeviceType = fields.ElementAtOrDefault(2) ?? "",
                        IpAddress = NullIfEmpty(fields.ElementAtOrDefault(3)),
                        SubnetMask = NullIfEmpty(fields.ElementAtOrDefault(4)),
                        MacAddress = NullIfEmpty(fields.ElementAtOrDefault(5)),
                        Role = NullIfEmpty(fields.ElementAtOrDefault(6))
                    };
                    devices.Add(current);
                }

                if (current == null)
                {
                    continue; // a continuation row before any device header showed up - ignore
                }

                var portId = fields.ElementAtOrDefault(18);
                if (!string.IsNullOrWhiteSpace(portId))
                {
                    current.Ports.Add(new PronetaPort
                    {
                        PortId = portId,
                        PortDescription = NullIfEmpty(fields.ElementAtOrDefault(19)),
                        PartnerPortId = NullIfEmpty(fields.ElementAtOrDefault(20)),
                        PartnerDeviceName = NullIfEmpty(fields.ElementAtOrDefault(21))
                    });
                }
            }

            return devices;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // Minimal RFC4180-ish CSV line parser (handles quoted fields, "" as an escaped quote) -
        // a plain Split(',') would break since every field in this export is quoted.
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }

        #endregion
    }

    // One device row from a PRONETA "Device Table (CSV)" export (with "Include device details").
    public class PronetaDevice
    {
        public string Name { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string? IpAddress { get; set; }
        public string? SubnetMask { get; set; }
        public string? MacAddress { get; set; }
        public string? Role { get; set; }
        public List<PronetaPort> Ports { get; set; } = [];
    }

    public class PronetaPort
    {
        public string PortId { get; set; } = "";
        public string? PortDescription { get; set; }
        public string? PartnerPortId { get; set; }
        public string? PartnerDeviceName { get; set; }
    }

    // Result of comparing one PRONETA device row against the currently open TIA project.
    public class NetworkImportMatch
    {
        public PronetaDevice PronetaDevice { get; set; } = null!;
        public string? MatchedTiaDeviceName { get; set; }
        public string? MatchMethod { get; set; }
        public string? TiaCurrentIp { get; set; }
        public string? TiaCurrentSubnetMask { get; set; }
        public bool IpDiffers { get; set; }
    }
}
