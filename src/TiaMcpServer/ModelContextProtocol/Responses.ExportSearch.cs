using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseSearchExports : ResponseMessage
    {
        /// <summary>All matches, also when 'matches' is cut at maxMatches.</summary>
        public int MatchCount { get; set; }

        public int FilesSearched { get; set; }

        public int FilesWithMatches { get; set; }

        public List<ResponseExportMatch>? Matches { get; set; }

        public List<string>? Unreadable { get; set; }
    }

    public class ResponseExportMatch
    {
        /// <summary>Path relative to the searched folder.</summary>
        public string File { get; set; } = string.Empty;

        public int Line { get; set; }

        /// <summary>1-based network (SW.Blocks.CompileUnit) of an XML block export; empty outside networks.</summary>
        public int? Network { get; set; }

        /// <summary>'symbol' (a joined access path), 'line' (non-XML file), or the XML element and attribute that matched.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }
}
