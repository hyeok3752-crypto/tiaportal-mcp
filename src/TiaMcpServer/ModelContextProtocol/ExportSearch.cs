using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Text search over an export folder (SearchExports). Pure file reading, no TIA Portal.
    ///
    /// SimaticML splits a symbolic access like "G2U1Assy".PLC_Ins.Pitch over one &lt;Component&gt;
    /// element per part, so a plain text search never finds it. XML files are therefore read with an
    /// XmlReader: every &lt;Symbol&gt; is joined back to 'A.B.C' and matched as one string, and all other
    /// attribute values and texts (member names, comments, titles, constants) are matched as they are.
    /// The match also names the network (1-based count of SW.Blocks.CompileUnit) it is in.
    /// Other text files are searched line by line.
    /// </summary>
    public static class ExportSearch
    {
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xml", ".scl", ".s7dcl", ".s7res", ".db", ".udt", ".awl", ".csv", ".txt", ".json", ".yml", ".yaml"
        };

        public sealed class Match
        {
            public string File { get; set; } = string.Empty;
            public int Line { get; set; }
            public int? Network { get; set; }
            public string Kind { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }

        public sealed class Result
        {
            public List<Match> Matches { get; } = new List<Match>();
            public int MatchCount { get; set; }
            public int FilesSearched { get; set; }
            public int FilesWithMatches { get; set; }
            public List<string> Unreadable { get; } = new List<string>();
        }

        /// <summary>Quotes and the '#' of locals are dropped, so '"G2U1Assy".PLC_Ins' and 'G2U1Assy.PLC_Ins' match alike.</summary>
        public static string Normalize(string text) => text.Replace("\"", string.Empty).Replace("#", string.Empty);

        public static Result Search(string root, Func<string, bool> fileFilter, Func<string, bool> isMatch, int maxMatches)
        {
            var result = new Result();
            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var relative = file.Substring(full.Length + 1).Replace('\\', '/');

                if (!TextExtensions.Contains(Path.GetExtension(file)) || !fileFilter(relative))
                {
                    continue;
                }

                result.FilesSearched++;
                var before = result.MatchCount;

                try
                {
                    if (string.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        SearchXml(file, relative, isMatch, result, maxMatches);
                    }
                    else
                    {
                        SearchLines(file, relative, isMatch, result, maxMatches);
                    }
                }
                catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Unreadable.Add($"{relative}: {ex.Message}");
                }

                if (result.MatchCount > before)
                {
                    result.FilesWithMatches++;
                }
            }

            return result;
        }

        private static void Add(Result result, int maxMatches, Match match)
        {
            result.MatchCount++;

            if (result.Matches.Count < maxMatches)
            {
                match.Text = match.Text.Length > 200 ? match.Text.Substring(0, 200) + "..." : match.Text;
                result.Matches.Add(match);
            }
        }

        private static void SearchLines(string file, string relative, Func<string, bool> isMatch, Result result, int maxMatches)
        {
            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
                number++;

                if (isMatch(Normalize(line)))
                {
                    Add(result, maxMatches, new Match { File = relative, Line = number, Kind = "line", Text = line.Trim() });
                }
            }
        }

        private static void SearchXml(string file, string relative, Func<string, bool> isMatch, Result result, int maxMatches)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };

            using (var reader = XmlReader.Create(file, settings))
            {
                var info = (IXmlLineInfo)reader;
                var network = 0;
                var symbolDepth = -1;
                var symbolLine = 0;
                var symbol = new List<string>();
                var element = string.Empty;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        element = reader.LocalName;

                        if (element == "SW.Blocks.CompileUnit")
                        {
                            network++;
                        }

                        if (element == "Symbol" && symbolDepth < 0)
                        {
                            symbolDepth = reader.Depth;
                            symbolLine = info.LineNumber;
                            symbol.Clear();
                        }
                        else if (symbolDepth >= 0 && element == "Component" && reader.Depth == symbolDepth + 1)
                        {
                            symbol.Add(reader.GetAttribute("Name") ?? string.Empty);
                        }
                        else if (symbolDepth < 0 && reader.HasAttributes)
                        {
                            for (var i = 0; i < reader.AttributeCount; i++)
                            {
                                reader.MoveToAttribute(i);

                                if (isMatch(reader.Value))
                                {
                                    Add(result, maxMatches, new Match
                                    {
                                        File = relative, Line = info.LineNumber, Network = network > 0 ? network : (int?)null,
                                        Kind = $"{element} {reader.LocalName}", Text = reader.Value
                                    });
                                }
                            }

                            reader.MoveToElement();
                        }

                        if (reader.IsEmptyElement && reader.Depth == symbolDepth)
                        {
                            symbolDepth = -1;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Symbol" && reader.Depth == symbolDepth)
                    {
                        var path = string.Join(".", symbol);
                        symbolDepth = -1;

                        if (isMatch(path))
                        {
                            Add(result, maxMatches, new Match
                            {
                                File = relative, Line = symbolLine, Network = network > 0 ? network : (int?)null, Kind = "symbol", Text = path
                            });
                        }
                    }
                    else if ((reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA) && symbolDepth < 0 && isMatch(Normalize(reader.Value)))
                    {
                        Add(result, maxMatches, new Match
                        {
                            File = relative, Line = info.LineNumber, Network = network > 0 ? network : (int?)null,
                            Kind = element, Text = reader.Value.Trim()
                        });
                    }
                }
            }
        }
    }
}
