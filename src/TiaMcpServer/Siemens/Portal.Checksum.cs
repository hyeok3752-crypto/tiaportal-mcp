using Siemens.Engineering.SW;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Software checksum (Openness manual V19, 5.11.2.2): one value for the compiled program of
        // a PLC, the same one TIA shows and the CPU reports online. Equal checksums mean the same
        // compiled program, so it answers "did the program change?" and "is this what runs on
        // the CPU?" in one read. TIA returns null until the program is compiled.

        public sealed class SoftwareChecksums
        {
            /// <summary>False when the PLC does not support checksums (no PlcChecksumProvider).</summary>
            public bool Supported { get; set; }
            public string? Software { get; set; }
            public string? TextLists { get; set; }
        }

        public SoftwareChecksums GetSoftwareChecksum(string softwarePath)
        {
            return Operation.Run(_logger, nameof(GetSoftwareChecksum), PortalErrorCode.NotFound,
                () =>
                {
                    var provider = GetPlcSoftwareOrThrow(softwarePath).GetService<PlcChecksumProvider>();
                    if (provider == null)
                    {
                        return new SoftwareChecksums { Supported = false };
                    }

                    return new SoftwareChecksums
                    {
                        Supported = true,
                        Software = provider.Software,
                        TextLists = provider.TextLists
                    };
                },
                ("softwarePath", softwarePath));
        }
    }
}
