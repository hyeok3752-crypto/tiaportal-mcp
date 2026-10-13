using System;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Progress of the one long-running, iterative tool call currently active on a
    /// PortalContext, if any. Set by the running tool via Portal.BeginProgress/ReportProgress/
    /// EndProgress, read by 'GetState' without taking the context's Gate so it stays visible
    /// while the call is still running.
    ///
    /// This exists only to give a human something to judge by ("is it stuck or just slow") -
    /// nothing in this codebase acts on it automatically (no auto-cancel, no auto-skip).
    /// </summary>
    public sealed class OperationProgress
    {
        public string Tool { get; }

        public DateTime StartedAt { get; }

        /// <summary>Total item count, when known upfront. Null when the tool can't size the work before starting.</summary>
        public int? Total { get; }

        public int Processed { get; }

        public string? CurrentPath { get; }

        public DateTime LastProgressAt { get; }

        public OperationProgress(string tool, DateTime startedAt, int? total, int processed, string? currentPath, DateTime lastProgressAt)
        {
            Tool = tool;
            StartedAt = startedAt;
            Total = total;
            Processed = processed;
            CurrentPath = currentPath;
            LastProgressAt = lastProgressAt;
        }

        internal OperationProgress WithProgress(int processed, string? currentPath, DateTime now) =>
            new OperationProgress(Tool, StartedAt, Total, processed, currentPath ?? CurrentPath, now);

        /// <summary>Same run, now sized - for tools that first list their work, then process it.</summary>
        internal OperationProgress WithTotal(int? total, DateTime now) =>
            new OperationProgress(Tool, StartedAt, total, 0, null, now);

        public string Describe()
        {
            var countPart = Total is int total ? $"{Processed}/{total}" : Processed.ToString();
            var pathPart = string.IsNullOrEmpty(CurrentPath) ? string.Empty : $", current: {CurrentPath}";

            return $"{Tool} running for {FormatSpan(DateTime.Now - StartedAt)}, {countPart}{pathPart}, " +
                   $"last progress {FormatSpan(DateTime.Now - LastProgressAt)} ago";
        }

        private static string FormatSpan(TimeSpan span)
        {
            var seconds = Math.Max(0, span.TotalSeconds);

            return seconds >= 60
                ? $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s"
                : $"{(int)seconds}s";
        }
    }
}
