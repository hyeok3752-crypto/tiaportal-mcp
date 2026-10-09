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

                //foreach (var group in _project.UngroupedDevicesGroup)
                //{
                //    GetDevicesRecursive(_project.UngroupedDevicesGroup, list, regexName);
                //}
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
}
