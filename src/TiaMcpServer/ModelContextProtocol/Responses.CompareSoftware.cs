using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseCompareSoftware : ResponseMessage
    {
        /// <summary>'online' or the other software path.</summary>
        public string? ComparedWith { get; set; }

        /// <summary>TIA's state of the whole comparison, e.g. FolderContentsIdentical or FolderContentsDifferent.</summary>
        public string? RootState { get; set; }

        public bool Identical { get; set; }

        /// <summary>All differing objects, also when 'differences' is cut at maxItems.</summary>
        public int DifferenceCount { get; set; }

        public List<ResponseSoftwareDifference>? Differences { get; set; }

        public string? StoppedReason { get; set; }
    }

    public class ResponseSoftwareDifference
    {
        /// <summary>Path in the compare tree, e.g. 'Program blocks/009_Servo/FB_PitchHome_Inch'.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>ObjectsDifferent, LeftMissing (only on the other side / online), RightMissing (only in this software), ...</summary>
        public string State { get; set; } = string.Empty;

        public string? LeftName { get; set; }

        public string? RightName { get; set; }

        public string? Detail { get; set; }
    }

    public class ResponseArchiveProject : ResponseMessage
    {
        public string? ArchiveFile { get; set; }

        public long? SizeBytes { get; set; }

        public string? Mode { get; set; }
    }
}
