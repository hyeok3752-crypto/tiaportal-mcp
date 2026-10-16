using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseSafetyInfo : ResponseMessage
    {
        /// <summary>False when the PLC has no SafetyAdministration (not an F-CPU); everything else is then empty.</summary>
        public bool IsFailsafeCpu { get; set; }

        public bool? PasswordSet { get; set; }

        public bool? LoggedOn { get; set; }

        public Dictionary<string, string?>? Settings { get; set; }

        public List<ResponseSafetyRuntimeGroup>? RuntimeGroups { get; set; }

        /// <summary>F-blocks with their offline signature; standard blocks are not listed.</summary>
        public List<ResponseSafetyBlockSignature>? Blocks { get; set; }

        public int BlocksChecked { get; set; }

        public int FBlockCount { get; set; }

        /// <summary>F-blocks whose signature is 0: no signature yet, or changed since the last compile.</summary>
        public int InvalidCount { get; set; }

        /// <summary>
        /// Our own hash over all listed F-block signatures (not TIA's F-collective signature). Same
        /// value as an earlier call = no F-block signature changed. Empty when the walk stopped early.
        /// </summary>
        public string? SignatureHash { get; set; }

        public string? StoppedReason { get; set; }

        public List<string>? Warnings { get; set; }
    }

    public class ResponseSafetyRuntimeGroup
    {
        public string? Name { get; set; }
        public string? FobName { get; set; }
        public string? FobEventClass { get; set; }
        public string? MainSafetyBlockName { get; set; }
        public string? MainSafetyBlockIDbName { get; set; }
        public string? InfoDbName { get; set; }
        public string? MaximumCycleTime { get; set; }
        public string? WarnCycleTime { get; set; }
    }

    public class ResponseSafetyBlockSignature
    {
        public string Path { get; set; } = string.Empty;

        /// <summary>Offline signature as TIA shows it, e.g. 16#1A2B3C4D.</summary>
        public string Signature { get; set; } = string.Empty;

        public bool Valid { get; set; }
    }
}
