using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.Safety;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region devices

        public List<Device> GetDevices(string regexName = "")
        {
            _logger?.LogInformation("Getting devices...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<Device>();

            if (_project?.Devices != null)
            {
                foreach (Device device in _project.Devices)
                {
                    list.Add(device);
                }

                foreach (var group in _project.DeviceGroups)
                {
                    GetDevicesRecursive(group, list, regexName);
                }

                // "Ungrouped devices" in the project tree - a DeviceSystemGroup, not a
                // DeviceUserGroup, so it can't go through GetDevicesRecursive (different type,
                // no subgroups of its own). Live-tested on a real project: GSD-based devices,
                // SCALANCE switches and ET200SP/eco stations can live only here - this was
                // silently dropping them from every GetDevices() caller (previously commented
                // out, presumably because UngroupedDevicesGroup isn't itself IEnumerable).
                if (_project.UngroupedDevicesGroup?.Devices != null)
                {
                    foreach (Device device in _project.UngroupedDevicesGroup.Devices)
                    {
                        if (string.IsNullOrEmpty(regexName) || Regex.IsMatch(device.Name, regexName, RegexOptions.IgnoreCase))
                        {
                            list.Add(device);
                        }
                    }
                }
            }

            return list;
        }

        public Device? GetDevice(string devicePath)
        {
            _logger?.LogInformation($"Getting device by path: {devicePath}");

            if (IsProjectNull())
            {
                return null;
            }

            // Retrieve the device by its path
            return GetDeviceByPath(devicePath);
        }

        public DeviceItem? GetDeviceItem(string deviceItemPath)
        {
            _logger?.LogInformation($"Getting device item by path: {deviceItemPath}");

            if (IsProjectNull())
            {
                return null;
            }

            // Retrieve the device by its path
            return GetDeviceItemByPath(deviceItemPath);

        }

        // Lists every third-party (GSD-based) device/device item referenced by this project's
        // hardware config - e.g. non-Siemens PROFINET/PROFIBUS IO devices - along with the GSD
        // identity (GsdId/GsdName) needed to pre-install the same GSD file on another machine
        // before opening this project there. Scoped to one device if devicePath is given,
        // otherwise scans every device in the project. Only sees devices TIA already loaded
        // successfully - a missing GSD can make TIA fail to instantiate the device at all, in
        // which case it won't show up here either.
        public List<GsdReference> GetGsdDependencies(string? devicePath = null)
        {
            _logger?.LogInformation("Scanning for GSD-based device dependencies...");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var devices = new List<Device>();
            if (string.IsNullOrEmpty(devicePath))
            {
                devices.AddRange(GetDevices());
            }
            else
            {
                var device = GetDeviceByPath(devicePath)
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"Device not found at '{devicePath}'");
                devices.Add(device);
            }

            var results = new List<GsdReference>();
            foreach (var device in devices)
            {
                CollectGsdInfo(device, results);
            }

            return results;
        }

        private static void CollectGsdInfo(Device device, List<GsdReference> results)
        {
            var gsdDevice = device.GetService<GsdDevice>();
            if (gsdDevice != null)
            {
                results.Add(new GsdReference
                {
                    Path = device.Name,
                    Name = device.Name,
                    GsdId = gsdDevice.GsdId,
                    GsdName = gsdDevice.GsdName,
                    GsdType = gsdDevice.GsdType,
                    IsProfibus = gsdDevice.IsProfibus,
                    IsProfinet = gsdDevice.IsProfinet
                });
            }

            if (device.DeviceItems != null)
            {
                foreach (var item in device.DeviceItems)
                {
                    CollectGsdInfoFromDeviceItem(item, device.Name, results);
                }
            }
        }

        private static void CollectGsdInfoFromDeviceItem(DeviceItem deviceItem, string parentPath, List<GsdReference> results)
        {
            var path = $"{parentPath}/{deviceItem.Name}";

            var gsdItem = deviceItem.GetService<GsdDeviceItem>();
            if (gsdItem != null)
            {
                results.Add(new GsdReference
                {
                    Path = path,
                    Name = deviceItem.Name,
                    GsdId = gsdItem.GsdId,
                    GsdName = gsdItem.GsdName,
                    GsdType = gsdItem.GsdType,
                    IsProfibus = gsdItem.IsProfibus,
                    IsProfinet = gsdItem.IsProfinet
                });
            }

            if (deviceItem.DeviceItems != null)
            {
                foreach (var child in deviceItem.DeviceItems)
                {
                    CollectGsdInfoFromDeviceItem(child, path, results);
                }
            }
        }

        #endregion

        #region device master copies / hardware catalog search

        // Root-caused (not just worked around): live testing initially showed CreateDevice /
        // CreateDeviceWithItem producing an empty shell or failing outright with "wrong type"
        // when given the TypeIdentifier read off an already-placed device (e.g.
        // "System:Device.S71500" from GetDeviceInfo) - that's a query-time-only format the
        // creation APIs don't accept. FindHardwareCatalogEntries (TiaPortal.HardwareCatalog.Find)
        // returns a different, creation-ready format ("OrderNumber:6ES7 516-3AN01-0AB0/V2.1")
        // and CreateDeviceWithItem given THAT string produces a fully populated, functional
        // device - verified live: a CPU 1516-3 PN/DP created this way came up with real DP/
        // PROFINET interface DeviceItems, and cleanup (DeleteDeviceGroup) removed it completely.
        // MasterCopy-based creation (CreateFrom) remains a secondary route for projects that have
        // saved templates; this lists what's in the project's own library.
        public List<string> GetDeviceMasterCopyPaths()
        {
            _logger?.LogInformation("Getting device master copies from the project library...");

            if (IsProjectNull())
            {
                return [];
            }

            if (_project is not Project project)
            {
                throw new PortalException(PortalErrorCode.InvalidState, "The project library isn't available for a multiuser local session");
            }

            var results = new List<string>();
            var root = project.ProjectLibrary?.MasterCopyFolder;
            if (root != null)
            {
                CollectMasterCopyPaths(root.MasterCopies, root.Name, results);
                foreach (var folder in root.Folders)
                {
                    CollectMasterCopyPathsRecursive(folder, root.Name, results);
                }
            }

            return results;
        }

        private static void CollectMasterCopyPaths(MasterCopyComposition copies, string parentPath, List<string> results)
        {
            foreach (var copy in copies)
            {
                results.Add($"{parentPath}/{copy.Name}");
            }
        }

        private static void CollectMasterCopyPathsRecursive(MasterCopyUserFolder folder, string parentPath, List<string> results)
        {
            var path = $"{parentPath}/{folder.Name}";
            CollectMasterCopyPaths(folder.MasterCopies, path, results);

            foreach (var subfolder in folder.Folders)
            {
                CollectMasterCopyPathsRecursive(subfolder, path, results);
            }
        }

        // This is the source of a creation-ready TypeIdentifier - see the region comment above
        // for the confirmed CreateDevice/CreateDeviceWithItem finding. Search by article number,
        // order number fragment, or product name (e.g. "G120C PN", "CPU 1516"); each match's
        // TypeIdentifier is in the "OrderNumber:<article>/<version>" format the creation APIs
        // actually require, unlike the "System:Device.X" format GetDeviceInfo/GetDevices expose.
        public List<CatalogEntryInfo> FindHardwareCatalogEntries(string filter)
        {
            _logger?.LogInformation("Searching hardware catalog for '{Filter}'...", filter);

            if (IsPortalNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No TIA Portal instance is connected");
            }

            var catalog = _portal!.HardwareCatalog;
            var entries = catalog.Find(filter);

            var results = new List<CatalogEntryInfo>();
            foreach (var entry in entries)
            {
                results.Add(new CatalogEntryInfo
                {
                    TypeName = entry.TypeName,
                    TypeIdentifier = entry.TypeIdentifier,
                    TypeIdentifierNormalized = entry.TypeIdentifierNormalized,
                    ArticleNumber = entry.ArticleNumber,
                    Version = entry.Version,
                    CatalogPath = entry.CatalogPath
                });
            }

            return results;
        }

        #endregion

        #region get...by path

        private Device? GetDeviceByPath(string devicePath)
        {
            if (_project?.Devices == null || string.IsNullOrWhiteSpace(devicePath))
                return null;

            // A hardware PLC's Device.Name can itself contain '/' (e.g. 'S7-1500/ET200MP
            // station_1'), which isn't visible as such in the TIA Portal IDE. Always try an
            // exact whole-string match against real device names first, before treating '/' as
            // a path separator - otherwise a name like that gets mis-split into bogus
            // group/device segments and the device is reported as not found. Live-tested against
            // a real project with a device literally named this way - confirmed this bug is
            // inherited from upstream unchanged, not something the rebase introduced.
            var exactMatch = FindDeviceByFullName(devicePath);
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var pathSegments = devicePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (pathSegments.Length == 0)
            {
                return null;
            }

            // Try top-level device first
            if (pathSegments.Length == 1)
            {
                return _project.Devices.FirstOrDefault(d => d.Name.Equals(pathSegments[0], StringComparison.OrdinalIgnoreCase));
            }

            // Traverse device groups
            DeviceUserGroupComposition? groups = _project.DeviceGroups;
            DeviceUserGroup? group = groups?.FirstOrDefault(g => g.Name.Equals(pathSegments[0], StringComparison.OrdinalIgnoreCase));

            if (group is null)
            {
                return null;
            }

            for (int i = 1; i < pathSegments.Length; i++)
            {
                // Try to find device in current group
                var device = group.Devices.FirstOrDefault(d => d.Name.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    return device;
                }

                // Try to find subgroup
                group = group.Groups.FirstOrDefault(g => g.Name.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase));
                if (group is null)
                {
                    break;
                }
            }

            return null;
        }

        // Searches top-level devices, every device group (recursively), and the project's
        // "Ungrouped devices" system group for a device whose Name matches the given string
        // exactly - used by GetDeviceByPath (device names can contain '/') and by
        // Portal.Network.cs's network-interface path resolution (same reasoning). The
        // "Ungrouped devices" fallback is load-bearing, not optional - live-tested on a real
        // project where GSD/SCALANCE/ET200 devices live only under that group.
        private Device? FindDeviceByFullName(string name)
        {
            if (_project?.Devices == null)
            {
                return null;
            }

            var device = _project.Devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (device != null)
            {
                return device;
            }

            device = FindDeviceByFullNameInGroups(_project.DeviceGroups, name);
            if (device != null)
            {
                return device;
            }

            return _project.UngroupedDevicesGroup?.Devices?.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static Device? FindDeviceByFullNameInGroups(DeviceUserGroupComposition? groups, string name)
        {
            if (groups == null)
            {
                return null;
            }

            foreach (var group in groups)
            {
                var device = group.Devices?.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    return device;
                }

                var found = FindDeviceByFullNameInGroups(group.Groups, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private DeviceItem? GetDeviceItemByPath(string deviceItemPath)
        {
            if (_project is null || _project.Devices == null)
            {
                return null;
            }

            // Split the device path by '/' to get each device name  
            var pathSegments = deviceItemPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            DeviceItem? deviceItem = null;

            // initial devices and groups
            var devices = _project.Devices;
            var groups = _project.DeviceGroups;

            for (int index = 0; index < pathSegments.Length; index++)
            {
                deviceItem = GetDeviceItemFromDevice(pathSegments, devices, index);

                if (deviceItem is null)
                {
                    // search in groups
                    var group = groups?.FirstOrDefault(g => g.Name.Equals(pathSegments[index], StringComparison.OrdinalIgnoreCase));
                    if (group is not null)
                    {
                        devices = group.Devices;
                        if (devices != null)
                        {
                            deviceItem = GetDeviceItemFromDevice(pathSegments, devices, index + 1);
                        }

                        if (deviceItem is not null)
                        {
                            return deviceItem;
                        }

                        // not found, but on the path
                        groups = group.Groups;
                        devices = group.Devices;
                    }
                }
                else if (deviceItem is not null)
                {
                    return deviceItem;
                }
            }

            return deviceItem;
        }

        private static DeviceItem? GetDeviceItemFromDevice(string[] pathSegments, DeviceComposition? devices, int index)
        {
            string segment = pathSegments[index];
            string nextSegment = index + 1 < pathSegments.Length ? pathSegments[index + 1] : string.Empty;

            DeviceItem? deviceItem = null;

            // a pc based plc has a Device.Name = 'PC-System_1' or something like that, which is visible in the TIA-Portal IDE
            // use segment to find device
            var device = devices.FirstOrDefault(d => d.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (device != null)
            {
                // then use next segment to find device item
                deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name.Equals(nextSegment, StringComparison.OrdinalIgnoreCase));

            }

            // a hardware plc has a Device.Name = 'S7-1500/ET200MP-Station_1' or something like that, which is not visible in the TIA-Portal IDE
            if (device == null)
            {
                deviceItem = devices
                .SelectMany(d => d.DeviceItems)
                .FirstOrDefault(di => di.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            }

            return deviceItem;
        }

        #endregion

        #region get recursive ...

        private bool GetDevicesRecursive(DeviceUserGroup group, List<Device> list, string regexName = "")
        {
            var anySuccess = false;

            foreach (var composition in group.Devices)
            {
                if (composition is Device device)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(device.Name, regexName, RegexOptions.IgnoreCase))
                        {
                            continue; // Skip this device if it doesn't match the pattern
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid regex pattern, skip this device
                        continue;
                    }

                    list.Add(device);

                    anySuccess = true;
                }
            }

            foreach (var subgroup in group.Groups)
            {
                anySuccess = GetDevicesRecursive(subgroup, list, regexName);
            }

            return anySuccess;
        }

        #endregion
    }

    // One match from TiaPortal.HardwareCatalog.Find(filter). TypeIdentifier here is the
    // catalog's own string, which live testing suggests may differ in format from the
    // TypeIdentifier attribute read back off an already-placed Device (the latter appears to
    // omit the order-number/firmware-version suffix that DeviceComposition.Create/CreateWithItem
    // actually require) - this is what CreateDevice/CreateDeviceWithItem should be tried with.
    public class CatalogEntryInfo
    {
        public string TypeName { get; set; } = "";
        public string? TypeIdentifier { get; set; }
        public string? TypeIdentifierNormalized { get; set; }
        public string? ArticleNumber { get; set; }
        public string? Version { get; set; }
        public string? CatalogPath { get; set; }
    }

    // A single third-party (GSD-based) device or device item found in a project's hardware
    // config, with the catalog identity needed to install the matching GSD file elsewhere.
    public class GsdReference
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string? GsdId { get; set; }
        public string? GsdName { get; set; }
        public string? GsdType { get; set; }
        public bool IsProfibus { get; set; }
        public bool IsProfinet { get; set; }
    }
}
