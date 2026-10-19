using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Network-level block edits. Openness has no network object (see GetNetworkSource), so a
        // network is changed the way a person would do it by hand: export the block, replace,
        // insert or delete one SW.Blocks.CompileUnit in the XML, and import the block again.
        // What this adds over doing that by hand:
        //
        // - LAD/FBD part order. TIA refuses an import whose <Parts> are not "sorted according to
        //   the current flow" (live 2026-10-15). SortFlgNetParts rebuilds that order from the
        //   wires: depth-first from the power rail, in wire order, and a part with several inputs
        //   (e.g. an O box joining two branches) only after all parts feeding it. Checked against
        //   128 networks exported by TIA: it reproduced all 125 unedited ones exactly; the 3 it
        //   changed were our hand-edited test files with a part appended at the end - the case
        //   TIA rejected.
        // - IDs. Every ID attribute of the document is renumbered, so a network copied from
        //   another export cannot collide with the IDs already in the block.
        // - Backup. The block is exported to the backup folder first, like DeleteBlock.

        public sealed class NetworkEditResult
        {
            public string BlockPath { get; set; } = string.Empty;
            public int NetworksBefore { get; set; }
            public int NetworksAfter { get; set; }
            public int PartsReordered { get; set; }
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
            public string? PreviewFile { get; set; }
        }

        /// <param name="action">"replace", "insert" (before networkIndex; the network count appends) or "delete".</param>
        /// <param name="networkFile">For replace/insert: an XML file holding a SW.Blocks.CompileUnit, e.g. a block export; its first network is used.</param>
        /// <param name="sourceNetworkIndex">Which network of networkFile to take (0-based, default 0).</param>
        /// <param name="previewFile">When set, the edited block XML is written there and nothing is imported.</param>
        public NetworkEditResult EditBlockNetwork(string softwarePath, string blockPath, string action, int networkIndex,
            string networkFile, int sourceNetworkIndex, string previewFile)
        {
            return Operation.Run(_logger, nameof(EditBlockNetwork), PortalErrorCode.ImportFailed,
                () =>
                {
                    var mode = (action ?? string.Empty).Trim().ToLowerInvariant();
                    if (mode != "replace" && mode != "insert" && mode != "delete")
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown action '{action}'. Use 'replace', 'insert' or 'delete'.");
                    }

                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    if (!block.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Block '{block.Name}' is inconsistent; TIA Portal does not export inconsistent blocks. Compile it first ('CompileCodeBlock').");
                    }

                    if (block.Parent is not PlcBlockGroup parentGroup)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, $"Block '{block.Name}' has no block group to import into.");
                    }

                    XElement? incoming = null;
                    if (mode != "delete")
                    {
                        incoming = LoadCompileUnit(networkFile, sourceNetworkIndex);
                    }

                    var result = new NetworkEditResult { BlockPath = GetBlockPath(block) };

                    using var scope = new SourceScope(_logger);
                    var exportFile = Path.Combine(scope.Directory, $"{FileNames.Safe(block.Name)}.xml");
                    block.Export(new FileInfo(exportFile), ExportOptions.None);

                    var doc = XDocument.Load(exportFile);
                    var units = CompileUnits(doc);
                    result.NetworksBefore = units.Count;

                    var limit = mode == "insert" ? units.Count : units.Count - 1;
                    if (networkIndex < 0 || networkIndex > limit)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"networkIndex {networkIndex} is out of range: block '{block.Name}' has {units.Count} network(s), numbered from 0" +
                            (mode == "insert" ? $"; {units.Count} appends." : "."));
                    }

                    switch (mode)
                    {
                        case "delete":
                            if (units.Count == 1)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams, "A block needs at least one network; replace it instead.");
                            }

                            units[networkIndex].Remove();
                            break;

                        case "replace":
                            units[networkIndex].ReplaceWith(incoming!);
                            break;

                        default:
                            if (units.Count == 0)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams, $"Block '{block.Name}' has no networks to insert next to.");
                            }

                            if (networkIndex == units.Count)
                            {
                                units[units.Count - 1].AddAfterSelf(incoming!);
                            }
                            else
                            {
                                units[networkIndex].AddBeforeSelf(incoming!);
                            }

                            break;
                    }

                    if (incoming != null)
                    {
                        foreach (var net in incoming.Descendants().Where(e => e.Name.LocalName == "FlgNet"))
                        {
                            result.PartsReordered += SortFlgNetParts(net);
                        }
                    }

                    RenumberIds(doc);
                    result.NetworksAfter = CompileUnits(doc).Count;

                    if (!string.IsNullOrWhiteSpace(previewFile))
                    {
                        var directory = Path.GetDirectoryName(previewFile);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        doc.Save(previewFile);
                        result.PreviewFile = previewFile;
                        return result;
                    }

                    var editedFile = Path.Combine(scope.Directory, "edited", $"{FileNames.Safe(block.Name)}.xml");
                    Directory.CreateDirectory(Path.GetDirectoryName(editedFile)!);
                    doc.Save(editedFile);

                    var backup = BackupBlock(softwarePath, blockPath, nameof(EditBlockNetwork));
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    ImportXmlBlock(softwarePath, GetPlcBlockGroupPath(parentGroup, includeSystemRoot: false), editedFile);

                    return result;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("action", action), ("networkIndex", networkIndex));
        }

        private static List<XElement> CompileUnits(XDocument doc) =>
            doc.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit").ToList();

        private static XElement LoadCompileUnit(string networkFile, int sourceNetworkIndex)
        {
            if (string.IsNullOrWhiteSpace(networkFile))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "'networkFile' is required for replace and insert.");
            }

            if (!File.Exists(networkFile))
            {
                throw new PortalException(PortalErrorCode.NotFound, $"networkFile '{networkFile}' does not exist.");
            }

            XDocument source;
            try
            {
                source = XDocument.Load(networkFile);
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"networkFile '{networkFile}' is not readable XML: {ex.Message}", null, ex);
            }

            var units = CompileUnits(source);
            if (units.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"networkFile '{networkFile}' holds no <SW.Blocks.CompileUnit>. Use a block export (ExportXmlBlock) or a file with one network element.");
            }

            if (sourceNetworkIndex < 0 || sourceNetworkIndex >= units.Count)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"sourceNetworkIndex {sourceNetworkIndex} is out of range: networkFile has {units.Count} network(s).");
            }

            return new XElement(units[sourceNetworkIndex]);
        }

        /// <summary>Renumbers every ID attribute in document order (hex, from 0), as TIA's own exports do.</summary>
        private static void RenumberIds(XDocument doc)
        {
            var next = 0;
            foreach (var attribute in doc.Descendants().Select(e => e.Attribute("ID")).Where(a => a != null))
            {
                attribute!.Value = next.ToString("X", CultureInfo.InvariantCulture);
                next++;
            }
        }

        /// <summary>
        /// Puts the parts of a LAD/FBD network into flow order (see the header). Access elements
        /// keep their places; parts and calls are re-sorted within their own slots. Returns how
        /// many changed position (0 when the order was already right).
        /// </summary>
        internal static int SortFlgNetParts(XElement flgNet)
        {
            var parts = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Parts");
            var wires = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Wires");
            if (parts == null || wires == null)
            {
                return 0;
            }

            var children = parts.Elements().ToList();
            var slots = children.Select((e, i) => (e, i)).Where(x => x.e.Name.LocalName != "Access").Select(x => x.i).ToList();
            var nodes = slots.Select(i => children[i]).ToList();
            var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                var uid = (string?)node.Attribute("UId");
                if (uid != null && !byId.ContainsKey(uid))
                {
                    byId[uid] = node;
                }
            }

            var roots = new List<string>();
            var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var predecessors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var wire in wires.Elements())
            {
                var ends = wire.Elements().ToList();
                if (ends.Count < 2)
                {
                    continue;
                }

                var targets = ends.Skip(1)
                    .Where(e => e.Name.LocalName == "NameCon")
                    .Select(e => (string?)e.Attribute("UId"))
                    .Where(id => id != null && byId.ContainsKey(id))
                    .Select(id => id!)
                    .ToList();

                var source = ends[0];
                if (source.Name.LocalName == "Powerrail")
                {
                    roots.AddRange(targets);
                }
                else if (source.Name.LocalName == "NameCon" && (string?)source.Attribute("UId") is string from && byId.ContainsKey(from))
                {
                    if (!successors.TryGetValue(from, out var list))
                    {
                        successors[from] = list = new List<string>();
                    }

                    foreach (var to in targets)
                    {
                        list.Add(to);
                        if (!predecessors.TryGetValue(to, out var set))
                        {
                            predecessors[to] = set = new HashSet<string>(StringComparer.Ordinal);
                        }

                        set.Add(from);
                    }
                }
            }

            var order = new List<XElement>();
            var placed = new HashSet<string>(StringComparer.Ordinal);

            // Explicit stack instead of recursion: a network can hold 900+ parts.
            void Visit(string start, bool force)
            {
                var stack = new Stack<(string Id, bool Force)>();
                stack.Push((start, force));

                while (stack.Count > 0)
                {
                    var (id, f) = stack.Pop();
                    if (placed.Contains(id))
                    {
                        continue;
                    }

                    if (!f && predecessors.TryGetValue(id, out var before) && before.Any(p => !placed.Contains(p)))
                    {
                        continue;
                    }

                    placed.Add(id);
                    order.Add(byId[id]);

                    if (successors.TryGetValue(id, out var next))
                    {
                        for (var i = next.Count - 1; i >= 0; i--)
                        {
                            stack.Push((next[i], false));
                        }
                    }
                }
            }

            foreach (var root in roots)
            {
                Visit(root, false);
            }

            foreach (var id in byId.Keys.ToList())
            {
                Visit(id, false);
            }

            foreach (var id in byId.Keys.ToList())
            {
                Visit(id, true);
            }

            // Nodes without a UId keep their relative order at the end.
            order.AddRange(nodes.Where(n => !order.Contains(n)));

            var moved = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (!ReferenceEquals(children[slots[i]], order[i]))
                {
                    moved++;
                }

                children[slots[i]] = order[i];
            }

            if (moved > 0)
            {
                foreach (var child in parts.Elements().ToList())
                {
                    child.Remove();
                }

                foreach (var child in children)
                {
                    parts.Add(child);
                }
            }

            return moved;
        }
    }
}
