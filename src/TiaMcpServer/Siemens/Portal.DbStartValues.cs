using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Start values of data block members, read and written in place through
        // DataBlock.Interface.Members (Openness manual V19, 6.4.2.18 "Accessing DB value parameter
        // without export"), so a single offset or recipe value can change without exporting and
        // re-importing the whole DB. Works for global and instance DBs.
        //
        // Member paths: Openness addresses a nested member as "Struct.Member" and an array
        // element as "Array.Array[0]" (the element repeats the array's name). Callers may write
        // the usual "Array[0]" form; FindDbMember tries both.

        public sealed class DbStartValue
        {
            /// <summary>Path in the usual notation, e.g. "S[3].OriginOffset".</summary>
            public string Path { get; set; } = string.Empty;
            public string? DataType { get; set; }
            public string? StartValue { get; set; }
            public string? Error { get; set; }
        }

        public sealed class DbStartValues
        {
            public string BlockPath { get; set; } = string.Empty;
            public int Total { get; set; }
            public List<DbStartValue> Members { get; } = new List<DbStartValue>();
            public string? StoppedReason { get; set; }
        }

        public DbStartValues GetDbStartValues(string softwarePath, string blockPath, string regexName, int maxMembers, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetDbStartValues), PortalErrorCode.NotFound,
                () =>
                {
                    var (block, members) = RequireDataBlockMembers(softwarePath, blockPath);
                    var result = new DbStartValues { BlockPath = GetBlockPath(block) };
                    var regex = string.IsNullOrEmpty(regexName) ? null : new Regex(regexName, RegexOptions.IgnoreCase);
                    var clock = Stopwatch.StartNew();
                    var stack = new Stack<(Member Member, string Path)>();

                    foreach (var member in members.Reverse<Member>())
                    {
                        stack.Push((member, member.Name));
                    }

                    while (stack.Count > 0)
                    {
                        if (clock.Elapsed > budget)
                        {
                            result.StoppedReason = $"stopped after {budget.TotalSeconds:0} s; narrow 'regexName' for the rest";
                            break;
                        }

                        var (member, path) = stack.Pop();
                        result.Total++;
                        var display = DisplayMemberPath(path);

                        if (regex == null || regex.IsMatch(display))
                        {
                            if (result.Members.Count >= maxMembers)
                            {
                                result.StoppedReason = $"stopped at {maxMembers} members; narrow 'regexName' or raise 'maxMembers'";
                                break;
                            }

                            result.Members.Add(ReadStartValue(member, display));
                        }

                        var children = ChildMembers(member);
                        if (children != null)
                        {
                            foreach (var child in children.Reverse<Member>())
                            {
                                stack.Push((child, path + "." + child.Name));
                            }
                        }
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath));
        }

        /// <returns>The block path, the member path as found, and the start value before and after.</returns>
        public (string BlockPath, string MemberPath, string? Old, string? New, BackupResult Backup) SetDbStartValue(
            string softwarePath, string blockPath, string memberPath, string value)
        {
            return Operation.Run(_logger, nameof(SetDbStartValue), PortalErrorCode.InvalidParams,
                () =>
                {
                    var (block, members) = RequireDataBlockMembers(softwarePath, blockPath);
                    EnsureNotKnowHowProtected(block);

                    var member = FindDbMember(members, memberPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Member '{memberPath}' not found in '{block.Name}'. 'GetDbStartValues' lists the member paths.");

                    var old = ReadStartValue(member, memberPath).StartValue;
                    var backup = BackupBlock(softwarePath, blockPath, nameof(SetDbStartValue));

                    try
                    {
                        member.SetAttribute("StartValue", value);
                    }
                    catch (Exception ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"TIA Portal refused start value '{value}' for '{memberPath}': {Operation.DescribeChain(ex)}", null, ex);
                    }

                    return (GetBlockPath(block), memberPath, old, ReadStartValue(member, memberPath).StartValue, backup);
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("memberPath", memberPath));
        }

        private (PlcBlock Block, MemberComposition Members) RequireDataBlockMembers(string softwarePath, string blockPath)
        {
            var block = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

            if (block is not DataBlock dataBlock)
            {
                throw new PortalException(PortalErrorCode.NotSupported,
                    $"'{block.Name}' is a {block.GetType().Name}; start values exist only in data blocks (global or instance DB).");
            }

            var members = dataBlock.Interface?.Members
                ?? throw new PortalException(PortalErrorCode.NotSupported,
                    $"Data block '{block.Name}' exposes no members. Know-how protected blocks hide theirs.");

            return (block, members);
        }

        private static MemberComposition? ChildMembers(Member member)
        {
            try
            {
                return ((IEngineeringObject)member).GetComposition("Members") as MemberComposition;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DbStartValue ReadStartValue(Member member, string path)
        {
            var entry = new DbStartValue { Path = path };

            try
            {
                entry.DataType = member.GetAttribute("DataTypeName")?.ToString();
            }
            catch (Exception)
            {
                // Some members (e.g. inside system types) do not report it.
            }

            try
            {
                entry.StartValue = member.GetAttribute("StartValue")?.ToString();
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
            }

            return entry;
        }

        private static readonly Regex ArraySegment = new Regex(@"^(?<name>.+?)(?<index>\[[^\]]*\])$", RegexOptions.Compiled);

        /// <summary>"S.S[3].OriginOffset" (Openness) -> "S[3].OriginOffset".</summary>
        internal static string DisplayMemberPath(string openness)
        {
            var segments = openness.Split('.');
            var result = new List<string>();

            foreach (var segment in segments)
            {
                var match = ArraySegment.Match(segment);
                if (match.Success && result.Count > 0 && result[result.Count - 1] == match.Groups["name"].Value)
                {
                    result[result.Count - 1] = segment;
                }
                else
                {
                    result.Add(segment);
                }
            }

            return string.Join(".", result);
        }

        /// <summary>"S[3].OriginOffset" -> "S.S[3].OriginOffset" (Openness).</summary>
        internal static string OpennessMemberPath(string display)
        {
            var result = new List<string>();

            foreach (var segment in display.Split('.'))
            {
                var match = ArraySegment.Match(segment);
                if (match.Success)
                {
                    result.Add(match.Groups["name"].Value);
                }

                result.Add(segment);
            }

            return string.Join(".", result);
        }

        private static Member? FindDbMember(MemberComposition members, string memberPath)
        {
            var path = (memberPath ?? string.Empty).Trim().Trim('"');

            foreach (var candidate in new[] { path, OpennessMemberPath(path) }.Distinct())
            {
                try
                {
                    var found = members.Find(candidate);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch (Exception)
                {
                    // Find throws on some malformed paths; try the other notation.
                }
            }

            return null;
        }
    }
}
