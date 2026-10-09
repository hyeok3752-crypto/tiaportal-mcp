using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region device write CRUD

        // Not implemented: importing/installing a GSD file. Verified via a full-assembly method
        // search (every method whose name contains "Gsd", no verb filter) that no Install/Import/
        // Add/Register-shaped method exists anywhere in this Openness version - confirmed absent,
        // not just unused.

        public Device CreateDevice(string typeIdentifier, string name, string groupPath = "")
        {
            return Operation.Run(_logger, nameof(CreateDevice), PortalErrorCode.CreateFailed,
                () =>
                {
                    var (devices, _) = ResolveDeviceGroupPath(groupPath);
                    if (devices == null)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Device group not found at '{groupPath}'");
                    }

                    return devices.Create(typeIdentifier, name);
                },
                ("typeIdentifier", typeIdentifier), ("name", name), ("groupPath", groupPath));
        }

        // For devices that need both a station-level name and a first sub-item name in one call
        // (e.g. a CPU station, mirroring how existing devices are shaped: Device
        // "S7-1500/ET200MP station_1" containing DeviceItem "PLC_1"). Root-caused: CreateDevice
        // alone produces an empty shell or "wrong type" for drive/CPU-shaped catalog entries -
        // those are modeled by Siemens as a station+item pair and need CreateWithItem.
        public Device CreateDeviceWithItem(string typeIdentifier, string name, string deviceItemName, string groupPath = "")
        {
            return Operation.Run(_logger, nameof(CreateDeviceWithItem), PortalErrorCode.CreateFailed,
                () =>
                {
                    var (devices, _) = ResolveDeviceGroupPath(groupPath);
                    if (devices == null)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Device group not found at '{groupPath}'");
                    }

                    return devices.CreateWithItem(typeIdentifier, name, deviceItemName);
                },
                ("typeIdentifier", typeIdentifier), ("name", name), ("deviceItemName", deviceItemName), ("groupPath", groupPath));
        }

        // Dry-run by default (confirm=false): reports what would be deleted without touching the
        // project. Hardware config changes are more expensive to recover from than a block/tag
        // (physical wiring and GSD matching are entangled with it), so unlike block/tag deletes
        // this needs an explicit confirm=true on top of whatever confirmation the caller already
        // got from the user.
        public (bool Executed, string DeviceName, string TypeName) DeleteDevice(string devicePath, bool confirm = false)
        {
            return Operation.Run(_logger, nameof(DeleteDevice), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var device = GetDevice(devicePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"Device not found at '{devicePath}'");

                    var name = device.Name;
                    // GSD-based devices don't support "TypeName" at all (throws) - this is
                    // display-only for the response message, not load-bearing for the delete.
                    string typeName;
                    try
                    {
                        typeName = device.GetAttribute("TypeName") as string ?? "";
                    }
                    catch (Exception)
                    {
                        typeName = "";
                    }

                    if (!confirm)
                    {
                        return (false, name, typeName);
                    }

                    device.Delete();
                    return (true, name, typeName);
                },
                ("devicePath", devicePath), ("confirm", confirm));
        }

        public DeviceUserGroup CreateDeviceGroup(string parentGroupPath, string name)
        {
            return Operation.Run(_logger, nameof(CreateDeviceGroup), PortalErrorCode.CreateFailed,
                () =>
                {
                    var (_, groups) = ResolveDeviceGroupPath(parentGroupPath);
                    if (groups == null)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Device group not found at '{parentGroupPath}'");
                    }

                    return groups.Create(name);
                },
                ("parentGroupPath", parentGroupPath), ("name", name));
        }

        public void DeleteDeviceGroup(string groupPath)
        {
            Operation.Run(_logger, nameof(DeleteDeviceGroup), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var group = GetDeviceUserGroupByPath(groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"Device group not found at '{groupPath}'");

                    group.Delete();
                },
                ("groupPath", groupPath));
        }

        // Returns both the Devices and Groups compositions at a group path ("" = project root) -
        // CreateDevice/CreateDeviceWithItem need the former, CreateDeviceGroup needs the latter.
        private (DeviceComposition? Devices, DeviceUserGroupComposition? Groups) ResolveDeviceGroupPath(string groupPath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            if (_project is not Project project)
            {
                return (null, null);
            }

            if (string.IsNullOrWhiteSpace(groupPath))
            {
                return (project.Devices, project.DeviceGroups);
            }

            var segments = groupPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            DeviceUserGroupComposition? groups = project.DeviceGroups;
            DeviceUserGroup? group = null;

            foreach (var segment in segments)
            {
                group = groups?.FirstOrDefault(g => g.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    return (null, null);
                }

                groups = group.Groups;
            }

            return (group?.Devices, group?.Groups);
        }

        // Resolves the DeviceUserGroup object itself at a path (for delete, which needs the
        // group, not its child compositions). Root has no single object to delete, so an empty
        // path returns null (surfaces as NotFound) rather than a special "can't delete root" case.
        private DeviceUserGroup? GetDeviceUserGroupByPath(string groupPath)
        {
            if (_project is not Project project || string.IsNullOrWhiteSpace(groupPath))
            {
                return null;
            }

            var segments = groupPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            DeviceUserGroupComposition? groups = project.DeviceGroups;
            DeviceUserGroup? group = null;

            foreach (var segment in segments)
            {
                group = groups?.FirstOrDefault(g => g.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    return null;
                }

                groups = group.Groups;
            }

            return group;
        }

        #endregion
    }
}
