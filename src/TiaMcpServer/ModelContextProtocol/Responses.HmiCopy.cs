using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiScreenCopy : ResponseMessage
    {
        public string? ScreenName { get; set; }
        public ushort? ScreenNumber { get; set; }
        public int Items { get; set; }

        /// <summary>Properties written because they differed from the new item's defaults.</summary>
        public int Properties { get; set; }

        public int Texts { get; set; }
        public int Dynamizations { get; set; }
        public int Events { get; set; }

        /// <summary>Copied strings that 'replace' changed.</summary>
        public int Replaced { get; set; }

        /// <summary>What could not be read from the source screen, so was not copied.</summary>
        public List<string>? Skipped { get; set; }

        /// <summary>What was read but could not be applied to the copy.</summary>
        public List<string>? Warnings { get; set; }
    }
}
