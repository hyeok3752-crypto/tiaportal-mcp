using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiScreenCompare : ResponseMessage
    {
        /// <summary>True when nothing differs (after 'replace').</summary>
        public bool Same { get; set; }

        public List<string>? OnlyInFirst { get; set; }

        public List<string>? OnlyInSecond { get; set; }

        /// <summary>Number of differing values, also when 'differences' is cut at maxDifferences.</summary>
        public int DifferenceCount { get; set; }

        public List<HmiScreenDifference>? Differences { get; set; }

        public List<string>? Skipped { get; set; }
    }

    public class HmiScreenDifference
    {
        /// <summary>Item and property, e.g. 'Faceplate container_1.Interface[Servo_Data]'; 'screen.' for the screen itself.</summary>
        public string Path { get; set; } = string.Empty;

        public string? First { get; set; }

        public string? Second { get; set; }
    }
}
