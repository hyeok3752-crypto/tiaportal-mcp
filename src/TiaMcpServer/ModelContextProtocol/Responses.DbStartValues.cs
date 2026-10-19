using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseDbStartValues : ResponseMessage
    {
        public string BlockPath { get; set; } = string.Empty;

        /// <summary>Members looked at, matching or not.</summary>
        public int Total { get; set; }

        public List<ResponseDbStartValue> Members { get; set; } = new List<ResponseDbStartValue>();

        public string? StoppedReason { get; set; }
    }

    public class ResponseDbStartValue
    {
        /// <summary>Member path, e.g. "S[3].OriginOffset"; pass it as 'memberPath'.</summary>
        public string Path { get; set; } = string.Empty;

        public string? DataType { get; set; }

        /// <summary>Empty when no start value is set (the type's default applies).</summary>
        public string? StartValue { get; set; }

        public string? Error { get; set; }
    }

    public class ResponseDbStartValueSet : ResponseMessage
    {
        public string BlockPath { get; set; } = string.Empty;
        public string MemberPath { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        /// <summary>XML of the DB as it was before the change.</summary>
        public string? Backup { get; set; }

        public string? BackupWarning { get; set; }
    }
}
