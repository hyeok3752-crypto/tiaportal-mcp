using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseCaxMessage
    {
        public string? Message { get; set; }
        public string? State { get; set; }
        public DateTime? DateTime { get; set; }
    }

    public class ResponseCaxTransfer : ResponseMessage
    {
        public string? State { get; set; }
        public int? ErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public IEnumerable<ResponseCaxMessage>? Messages { get; set; }
        public string? LogPath { get; set; }
        public string? LogText { get; set; }
    }
}
