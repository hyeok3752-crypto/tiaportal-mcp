namespace TiaMcpServer.ModelContextProtocol
{
    // DeleteDevice has its own dry-run/confirm shape (unlike ResponseDeleted) - hardware config
    // deletes are more expensive to recover from than a block/tag, so this reports what would be
    // deleted without executing unless confirm=true.
    public class ResponseDeviceDelete : ResponseMessage
    {
        public bool Executed { get; set; }
        public string? DevicePath { get; set; }
        public string? DeviceName { get; set; }
        public string? TypeName { get; set; }
    }
}
