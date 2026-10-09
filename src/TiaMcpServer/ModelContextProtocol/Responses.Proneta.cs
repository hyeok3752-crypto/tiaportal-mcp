using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponsePronetaPort
    {
        public string? PortId { get; set; }
        public string? PartnerPortId { get; set; }
        public string? PartnerDeviceName { get; set; }
    }

    public class ResponseNetworkCsvMatch
    {
        public string? PronetaName { get; set; }
        public string? DeviceType { get; set; }
        public string? PronetaIp { get; set; }
        public string? PronetaSubnetMask { get; set; }
        public string? MacAddress { get; set; }
        public string? Role { get; set; }
        public string? MatchedTiaDeviceName { get; set; }
        public string? MatchMethod { get; set; }
        public string? TiaCurrentIp { get; set; }
        public string? TiaCurrentSubnetMask { get; set; }
        public bool? IpDiffers { get; set; }
        public IEnumerable<ResponsePronetaPort>? Ports { get; set; }
    }

    public class ResponseCompareNetworkCsv : ResponseMessage
    {
        public IEnumerable<ResponseNetworkCsvMatch>? Items { get; set; }
    }
}
