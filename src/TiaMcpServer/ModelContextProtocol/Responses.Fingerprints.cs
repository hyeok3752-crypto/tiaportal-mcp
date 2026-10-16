using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseBlockFingerprints : ResponseMessage
    {
        /// <summary>Blocks in the group/filter, also those not reached when the walk stopped early.</summary>
        public int Total { get; set; }

        public int Read { get; set; }

        /// <summary>The fingerprints; listed only without 'compareFile', and at most 'maxBlocks' of them.</summary>
        public List<ResponseBlockFingerprint>? Blocks { get; set; }

        /// <summary>With 'compareFile': blocks whose fingerprints differ from the saved ones.</summary>
        public List<ResponseFingerprintChange>? Changed { get; set; }

        /// <summary>With 'compareFile': blocks that are new since the saved file.</summary>
        public List<string>? Added { get; set; }

        /// <summary>With 'compareFile': blocks in the saved file that no longer exist (only when the walk was complete).</summary>
        public List<string>? Removed { get; set; }

        public int? Unchanged { get; set; }

        public string? SavedTo { get; set; }

        public string? StoppedReason { get; set; }

        public List<string>? Warnings { get; set; }
    }

    public class ResponseBlockFingerprint
    {
        public string Path { get; set; } = string.Empty;

        /// <summary>FingerprintId (Interface, ProgramCode, Comments, Properties, ...) -> checksum.</summary>
        public SortedDictionary<string, string>? Fingerprints { get; set; }

        public string? Error { get; set; }
    }

    public class ResponseFingerprintChange
    {
        public string Path { get; set; } = string.Empty;

        /// <summary>Which aspects changed, e.g. ProgramCode, Interface, Comments.</summary>
        public List<string> Aspects { get; set; } = new List<string>();
    }
}
