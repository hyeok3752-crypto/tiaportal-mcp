using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region device write CRUD

        [WriteTool]
        [McpServerTool(Name = "CreateDevice", Title = "Create device", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create a new single-item hardware device in the project. Most real device types (drives, CPU stations, HMI panels, and - unverified claims to the contrary in earlier notes aside - possibly switches too) are modeled by Siemens as a station+item pair and need CreateDeviceWithItem instead - this fails with 'the object cannot be created, it is of the wrong type' for those even with a correct TypeIdentifier, so try CreateDeviceWithItem if this fails. typeIdentifier MUST come from FindHardwareCatalogEntries (format 'OrderNumber:<article>/<version>') - the TypeIdentifier attribute GetDevices/GetDeviceInfo expose off an already-placed device (format 'System:Device.X') is query-only and is rejected by this API.")]
        public static ResponseCreated CreateDevice(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6SL3210-1KE11-8AF2/4.7.14' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device")] string name,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            return Guarded(nameof(CreateDevice), () =>
            {
                var device = Portal.CreateDevice(typeIdentifier, name, groupPath);
                return Created("Device", device.Name, JoinPath(groupPath, device.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateDeviceWithItem", Title = "Create device with item", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create a new hardware device that has both a station-level name and a first sub-item name in one call - e.g. a CPU station or a drive (mirrors how existing devices are shaped: Device 'S7-1500/ET200MP station_1' containing DeviceItem 'PLC_1'). Use this instead of CreateDevice for anything that isn't a bare single-item catalog entry - verified live for a CPU 1516-3 PN/DP, which came up with real DP/PROFINET interface DeviceItems, not an empty shell. typeIdentifier MUST come from FindHardwareCatalogEntries (format 'OrderNumber:<article>/<version>') - the TypeIdentifier attribute GetDevices/GetDeviceInfo expose off an already-placed device (format 'System:Device.X') is query-only and is rejected by this API ('the object cannot be created, it is of the wrong type'). Note: the resulting Device.Name mirrors deviceItemName, not name - the 'name' argument does not become the queryable device name in practice.")]
        public static ResponseCreated CreateDeviceWithItem(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6ES7 516-3AN01-0AB0/V2.1' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device (station) - note this does not end up as the queryable Device.Name; use deviceItemName for the path you'll query/reference afterward")] string name,
            [Description("deviceItemName: name for the first sub-item (e.g. the CPU module) - this is what the resulting device is actually named/found under")] string deviceItemName,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            return Guarded(nameof(CreateDeviceWithItem), () =>
            {
                var device = Portal.CreateDeviceWithItem(typeIdentifier, name, deviceItemName, groupPath);
                return Created("Device", device.Name, JoinPath(groupPath, device.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteDevice", Title = "Delete device", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a hardware device from the project. DRY-RUN BY DEFAULT: without confirm=true, this only reports what device would be deleted and changes nothing - call it once to preview, then again with confirm=true to actually delete. TIA's own undo only survives until the next save, and hardware config is entangled with physical wiring/GSD matching, so recovery is more expensive than a block/tag delete - confirm with the user before ever passing confirm=true against a real (non-disposable) project.")]
        public static ResponseDeviceDelete DeleteDevice(
            [Description("devicePath: full path to the device")] string devicePath,
            [Description("confirm: must be explicitly true to actually delete - false (default) only previews what would be deleted")] bool confirm = false)
        {
            return Guarded(nameof(DeleteDevice), () =>
            {
                var (executed, deviceName, typeName) = Portal.DeleteDevice(devicePath, confirm);

                return new ResponseDeviceDelete
                {
                    Message = executed
                        ? $"Device '{deviceName}' ({typeName}) deleted. {SaveHint}"
                        : $"Preview only (confirm=false, nothing changed) - would delete device '{deviceName}' ({typeName})",
                    Executed = executed,
                    DevicePath = devicePath,
                    DeviceName = deviceName,
                    TypeName = typeName,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateDeviceGroup", Title = "Create device group", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create a new device group (folder) in the project, for organizing devices.")]
        public static ResponseCreated CreateDeviceGroup(
            [Description("parentGroupPath: path to the parent group to create the new group under, e.g. 'Group/Subgroup' (empty for the project root)")] string parentGroupPath,
            [Description("name: name for the new group")] string name)
        {
            return Guarded(nameof(CreateDeviceGroup), () =>
            {
                var group = Portal.CreateDeviceGroup(parentGroupPath, name);
                return Created("Device group", group.Name, JoinPath(parentGroupPath, group.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteDeviceGroup", Title = "Delete device group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a device group (folder) from the project - also deletes every device inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleted DeleteDeviceGroup(
            [Description("groupPath: full path to the group, e.g. 'Group/Subgroup'")] string groupPath)
        {
            return Guarded(nameof(DeleteDeviceGroup), () =>
            {
                Portal.DeleteDeviceGroup(groupPath);
                return Deleted("Device group", groupPath);
            });
        }

        #endregion
    }
}
