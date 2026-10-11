using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Online;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region online connection (engineering station)

        // Openness's Online.OnlineProvider service exposes the engineering station's online
        // connection independently of the PLC's own Run/Stop state - GetService<OnlineProvider>()
        // on a Device or DeviceItem gives a plain public .State property and GoOnline()/
        // GoOffline() methods. None of GetOnlineState/GoOnline/GoOffline start, stop, or
        // otherwise command the PLC - only the engineering station's own online/diagnostic
        // connection changes. Deliberately does not add Run/Stop control - real PLC state
        // control stays out of scope as a safety call (see TODO.md).

        // Tries a Device first (e.g. a whole station like 'S7-1500/ET200MP station_1'), then a
        // DeviceItem (e.g. the CPU module within it, like 'PLC_1'), mirroring how GetDevice/
        // GetDeviceItem are both supported.
        private OnlineProvider? GetOnlineProvider(string path)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var device = GetDevice(path);
            var provider = device?.GetService<OnlineProvider>();
            if (provider != null)
            {
                return provider;
            }

            var deviceItem = GetDeviceItem(path);
            return deviceItem?.GetService<OnlineProvider>();
        }

        public OnlineState? GetOnlineState(string path)
        {
            _logger?.LogInformation($"Getting online state: {path}");
            return GetOnlineProvider(path)?.State;
        }

        public OnlineState GoOnline(string path)
        {
            _logger?.LogInformation($"Going online: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            return provider.GoOnline();
        }

        public void GoOffline(string path)
        {
            _logger?.LogInformation($"Going offline: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            provider.GoOffline();
        }

        #endregion
    }
}
