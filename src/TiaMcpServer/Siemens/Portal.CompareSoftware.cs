using Siemens.Engineering.Compare;
using Siemens.Engineering.SW;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // TIA's own compare (Openness manual V19, 5.11.1.13): PlcSoftware.CompareTo(other PLC
        // software) or CompareToOnline() against the connected PLC. The result is a tree of
        // CompareResultElement (LeftName, ComparisonResult, RightName, DetailedInformation); only the
        // elements that differ are kept, and identical folders are not walked.
        // Reads only - CompareToOnline needs the online connection but does not download anything.

        public sealed class SoftwareDifference
        {
            public string Path { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? LeftName { get; set; }
            public string? RightName { get; set; }
            public string? Detail { get; set; }
        }

        public sealed class SoftwareComparison
        {
            public string RootState { get; set; } = string.Empty;
            public List<SoftwareDifference> Differences { get; } = new List<SoftwareDifference>();
            public int DifferenceCount { get; set; }
            public string? StoppedReason { get; set; }
        }

        public SoftwareComparison CompareSoftware(string softwarePath, string otherSoftwarePath, int maxItems, TimeSpan budget)
        {
            var online = string.IsNullOrWhiteSpace(otherSoftwarePath);

            return Operation.Run(_logger, nameof(CompareSoftware), PortalErrorCode.InvalidState,
                () =>
                {
                    var plcSoftware = RequireSoftwareContainer(softwarePath).Software as PlcSoftware
                        ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{softwarePath}' is not a PLC software.");

                    CompareResult result;

                    if (online)
                    {
                        try
                        {
                            result = plcSoftware.CompareToOnline();
                        }
                        catch (Exception ex)
                        {
                            throw new PortalException(PortalErrorCode.InvalidState,
                                $"TIA Portal could not compare '{softwarePath}' with the online PLC: {Operation.DescribeChain(ex)}. " +
                                "The PLC must be reachable; 'GoOnline' on the PLC device item first if TIA Portal asks for the connection.",
                                null, ex);
                        }
                    }
                    else
                    {
                        var other = RequireSoftwareContainer(otherSoftwarePath).Software as PlcSoftware
                            ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{otherSoftwarePath}' is not a PLC software.");

                        result = plcSoftware.CompareTo(other);
                    }

                    var comparison = new SoftwareComparison();
                    var root = result.RootElement;
                    comparison.RootState = root.ComparisonResult.ToString();

                    var clock = Stopwatch.StartNew();
                    Walk(root.Elements, string.Empty, comparison, Math.Max(0, maxItems), clock, budget);

                    return comparison;
                },
                ("softwarePath", softwarePath), ("otherSoftwarePath", online ? "(online)" : otherSoftwarePath));
        }

        private static void Walk(CompareResultElementComposition elements, string parentPath, SoftwareComparison comparison,
            int maxItems, Stopwatch clock, TimeSpan budget)
        {
            foreach (var element in elements)
            {
                if (comparison.StoppedReason != null)
                {
                    return;
                }

                if (clock.Elapsed > budget)
                {
                    comparison.StoppedReason = $"stopped after {budget.TotalSeconds:0} s";
                    return;
                }

                var state = element.ComparisonResult;

                if (state == CompareResultState.ObjectsIdentical ||
                    state == CompareResultState.FolderContentsIdentical ||
                    state == CompareResultState.CompareIrrelevant)
                {
                    continue;
                }

                var name = !string.IsNullOrEmpty(element.LeftName) ? element.LeftName : element.RightName;
                var path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
                var isFolder = state == CompareResultState.FolderContentsDifferent ||
                               state == CompareResultState.FolderContainsDifferencesOwnStateDifferent;

                // Folders only lead to the objects that differ; listing them too would double every
                // entry. A folder whose own state differs is an object difference in itself.
                if (!isFolder || state == CompareResultState.FolderContainsDifferencesOwnStateDifferent)
                {
                    comparison.DifferenceCount++;

                    if (comparison.Differences.Count < maxItems)
                    {
                        comparison.Differences.Add(new SoftwareDifference
                        {
                            Path = path,
                            State = state.ToString(),
                            LeftName = element.LeftName,
                            RightName = element.RightName,
                            Detail = string.IsNullOrWhiteSpace(element.DetailedInformation) ? null : element.DetailedInformation
                        });
                    }
                }

                Walk(element.Elements, path, comparison, maxItems, clock, budget);
            }
        }
    }
}
