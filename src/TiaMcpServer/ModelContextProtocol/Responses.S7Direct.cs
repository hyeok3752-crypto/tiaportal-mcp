using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    // Direct PLC (S7CommPlus) diagnostics - bypasses Openness, see THIRD_PARTY_LICENSES.md.
    public class ResponseS7Connect : ResponseMessage
    {
    }

    public class ResponseS7TagBrowseEntry
    {
        public string? Name { get; set; }
        public string? DataType { get; set; }
        public string? AccessSequence { get; set; }
    }

    public class ResponseS7TagBrowse : ResponseMessage
    {
        public IEnumerable<ResponseS7TagBrowseEntry>? Tags { get; set; }
    }

    public class ResponseS7TagValue
    {
        public string? Name { get; set; }
        public string? DataType { get; set; }
        public string? Value { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class ResponseS7TagValues : ResponseMessage
    {
        public IEnumerable<ResponseS7TagValue>? Values { get; set; }
    }

    public class ResponseS7Alarm
    {
        public ulong CpuAlarmId { get; set; }
        public int AlarmDomain { get; set; }
        public int MessageType { get; set; }
        public uint SequenceCounter { get; set; }
        public bool IsComing { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime AckTimestamp { get; set; }
        public string? AlarmText { get; set; }
        public string? InfoText { get; set; }
    }

    public class ResponseS7Alarms : ResponseMessage
    {
        public IEnumerable<ResponseS7Alarm>? Alarms { get; set; }
    }

    public class ResponseS7TagWriteResult
    {
        public string? Name { get; set; }
        public string? DataType { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public bool Written { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class ResponseS7TagWrites : ResponseMessage
    {
        public bool Written { get; set; }
        public IEnumerable<ResponseS7TagWriteResult>? Results { get; set; }
    }
}
