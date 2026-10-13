using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseCompareExports : ResponseMessage
    {
        /// <summary>Relative paths present only in pathA.</summary>
        public IEnumerable<string>? OnlyInA { get; set; }

        /// <summary>Relative paths present only in pathB.</summary>
        public IEnumerable<string>? OnlyInB { get; set; }

        /// <summary>Changed files, at most maxFiles of them.</summary>
        public IEnumerable<ResponseFileDiff>? Changed { get; set; }

        /// <summary>Number of changed files, including those beyond maxFiles.</summary>
        public int ChangedCount { get; set; }

        public int UnchangedCount { get; set; }
    }

    public class ResponseFileDiff
    {
        public string? Path { get; set; }

        /// <summary>Lines of the changed region in A with no counterpart in B.</summary>
        public int LinesRemoved { get; set; }

        /// <summary>Lines of the changed region in B with no counterpart in A.</summary>
        public int LinesAdded { get; set; }

        /// <summary>The changed region in A, 1-based line numbers, between the common start and end.</summary>
        public string? RegionA { get; set; }

        public string? RegionB { get; set; }

        public List<string>? ExcerptA { get; set; }

        public List<string>? ExcerptB { get; set; }

        public string? Note { get; set; }
    }
}
