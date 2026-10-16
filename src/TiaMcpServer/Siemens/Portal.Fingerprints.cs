using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Block fingerprints (Openness manual V19, 5.11.2.6): FingerprintProvider on a block returns
        // one checksum per aspect (Interface, ProgramCode, Comments, Properties, ...). They reflect
        // user input only, not compile results, so comparing two readings says which blocks someone
        // changed - without exporting a single XML. Only consistent blocks have valid fingerprints;
        // an inconsistent one is reported as such instead of failing the whole walk.

        public sealed class BlockFingerprints
        {
            public string Path { get; set; } = string.Empty;

            /// <summary>FingerprintId name -> checksum.</summary>
            public SortedDictionary<string, string> Values { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Why there are no values, e.g. "inconsistent - compile first".</summary>
            public string? Error { get; set; }
        }

        public sealed class FingerprintScan
        {
            public List<BlockFingerprints> Blocks { get; } = new List<BlockFingerprints>();
            public int Total { get; set; }
            public string? StoppedReason { get; set; }
        }

        public FingerprintScan GetBlockFingerprints(string softwarePath, string groupPath, string regexName, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetBlockFingerprints), PortalErrorCode.NotFound,
                () =>
                {
                    RequireSoftwareContainer(softwarePath);

                    var clock = Stopwatch.StartNew();
                    var blocks = CollectBlocks(softwarePath, groupPath, regexName);
                    var scan = new FingerprintScan { Total = blocks.Count };

                    BeginProgress(nameof(GetBlockFingerprints), blocks.Count);
                    try
                    {
                        foreach (var block in blocks)
                        {
                            if (clock.Elapsed > budget)
                            {
                                scan.StoppedReason = $"stopped after {budget.TotalSeconds:0} s at block {scan.Blocks.Count + 1} of {blocks.Count}; narrow 'groupPath' or 'regexName' for the rest";
                                break;
                            }

                            var entry = new BlockFingerprints { Path = GetBlockPath(block) };
                            scan.Blocks.Add(entry);
                            ReportProgress(scan.Blocks.Count, entry.Path);

                            try
                            {
                                if (!block.IsConsistent)
                                {
                                    entry.Error = "inconsistent - compile it first";
                                    continue;
                                }

                                var provider = block.GetService<FingerprintProvider>();
                                if (provider == null)
                                {
                                    entry.Error = "no fingerprints for this block";
                                    continue;
                                }

                                foreach (var fingerprint in provider.GetFingerprints())
                                {
                                    entry.Values[fingerprint.Id.ToString()] = fingerprint.Value;
                                }
                            }
                            catch (Exception ex)
                            {
                                entry.Error = Operation.DescribeChain(ex);
                            }
                        }
                    }
                    finally
                    {
                        EndProgress();
                    }

                    return scan;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath));
        }
    }
}
