using Microsoft.Extensions.Logging;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Safety;
using System;
using System.Linq;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region software

        public PlcSoftware? GetPlcSoftware(string softwarePath)
        {
            _logger?.LogInformation($"Getting software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                return plcSoftware;
            }

            return null;
        }

        public CompilerResult? CompileSoftware(string softwarePath, string password = "")
        {
            _logger?.LogInformation($"Compiling software by path: {softwarePath}");

            // Every failure used to come back as null, which the tool could only report as
            // "Failed compiling 'PLC_1'" - live, three times in a row, with no hint whether the
            // path, the safety login or Openness itself was the problem. Each case now throws
            // with its own reason; a compile that runs and finds errors still returns its result.
            return Operation.Run(_logger, nameof(CompileSoftware), PortalErrorCode.InvalidState,
                () =>
                {
                    if (IsProjectNull())
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
                    }

                    var softwareContainer = GetSoftwareContainer(softwarePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"No software found at '{softwarePath}'. 'GetProjectTree' lists the valid software paths.");

                    if (!string.IsNullOrEmpty(password))
                    {
                        var deviceItem = softwareContainer.Parent as DeviceItem;

                        var admin = deviceItem?.GetService<SafetyAdministration>();
                        if (admin != null && !admin.IsLoggedOnToSafetyOfflineProgram)
                        {
                            SecureString secString = new NetworkCredential("", password).SecurePassword;
                            try
                            {
                                admin.LoginToSafetyOfflineProgram(secString);
                            }
                            catch (Exception ex)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams,
                                    $"Login to the safety offline program failed: {ex.Message}", null, ex);
                            }
                        }
                    }

                    if (softwareContainer.Software is not PlcSoftware plcSoftware)
                    {
                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{softwarePath}' is not a PLC software ({softwareContainer.Software?.GetType().Name ?? "no software"}); only PLC software can be compiled here.");
                    }

                    var compileService = plcSoftware.GetService<ICompilable>()
                        ?? throw new PortalException(PortalErrorCode.NotSupported,
                            $"TIA Portal offers no compile service for '{softwarePath}'.");

                    try
                    {
                        return compileService.Compile();
                    }
                    catch (Exception ex)
                    {
                        // Typical live causes: a modal dialog open in the TIA Portal UI, or the
                        // project being edited there at the same moment.
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"TIA Portal refused to compile '{softwarePath}': {ex.GetType().Name}: {ex.Message}" +
                            (ex.InnerException != null ? $" ({ex.InnerException.Message})" : string.Empty) +
                            " - check for an open dialog or a running edit in the TIA Portal window.", null, ex);
                    }
                },
                ("softwarePath", softwarePath));
        }

        #endregion

        #region get software container ...

        private SoftwareContainer? GetSoftwareContainer(string softwarePath)
        {
            if (_project == null)
            {
                return null;
            }

            string[] pathSegments = softwarePath.Split('/');
            int index = 0;

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            // in Devices
            if (_project.Devices != null)
            {
                softwareContainer = GetSoftwareContainerInDevices(_project.Devices, pathSegments, index);
                if (softwareContainer is not null)
                {
                    return softwareContainer;
                }
            }

            // in Groups
            if (_project.DeviceGroups != null)
            {
                softwareContainer = GetSoftwareContainerInGroups(_project.DeviceGroups, pathSegments, index);
                if (softwareContainer is not null)
                {
                    return softwareContainer;
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDevices(DeviceComposition devices, string[] pathSegments, int index)
        {

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            string nextSegment = index + 1 < pathSegments.Length ? pathSegments[index + 1] : string.Empty;

            if (devices != null)
            {
                SoftwareContainer? softwareContainer = null;
                Device? device = null;
                DeviceItem? deviceItem = null;

                // a pc based plc has a Device.Name = 'PC-System_1' or something like that, which is visible in the TIA-Portal IDE
                // use segment to find device
                device = devices.FirstOrDefault(d => d.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (device is not null)
                {
                    // then use next segment to find device item
                    deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name.Equals(nextSegment, StringComparison.OrdinalIgnoreCase));
                    // but here we use next segment to find device item
                    softwareContainer = GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index + 1);
                    if (softwareContainer is not null)
                    {
                        return softwareContainer;
                    }
                }

                // a hardware plc has a Device.Name = 'S7-1500/ET200MP-Station_1' or something like that, which is not visible in the TIA-Portal IDE
                // ignored segment for Device.Name and use it for DeviceItem.Name
                deviceItem = devices
                    .SelectMany(d => d.DeviceItems)
                    .FirstOrDefault(di => di.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (deviceItem is not null)
                {
                    return GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index);
                }

            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInGroups(DeviceUserGroupComposition groups, string[] pathSegments, int index)
        {
            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            if (groups != null)
            {
                var group = groups.FirstOrDefault(g => g.Name.Equals(segment));
                if (group is not null)
                {
                    // when segment matched
                    softwareContainer = GetSoftwareContainerInDevices(group.Devices, pathSegments, index + 1);
                    if (softwareContainer is not null)
                    {
                        return softwareContainer;
                    }

                    return GetSoftwareContainerInGroups(group.Groups, pathSegments, index + 1);
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDeviceItem(DeviceItem deviceItem, string[] pathSegments, int index)
        {
            if (deviceItem != null)
            {
                // when segment matched
                if (index == pathSegments.Length - 1)
                {
                    // get from DeviceItem
                    var softwareContainer = deviceItem.GetService<SoftwareContainer>();
                    if (softwareContainer != null)
                    {
                        return softwareContainer;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
