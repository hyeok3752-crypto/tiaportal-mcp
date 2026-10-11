namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseOnlineState : ResponseMessage
    {
        public string? State { get; set; }
    }

    public class ResponseGoOffline : ResponseMessage
    {
    }
}
