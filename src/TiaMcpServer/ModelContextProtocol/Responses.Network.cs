using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseSubnetInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? NetType { get; set; }
        public string? TypeIdentifier { get; set; }
        public IEnumerable<string>? NodeNames { get; set; }
        public IEnumerable<string>? IoSystemNames { get; set; }
    }

    public class ResponseSubnets : ResponseMessage
    {
        public IEnumerable<ResponseSubnetInfo>? Items { get; set; }
    }

    public class ResponseNetworkNodeInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? ConnectedSubnetName { get; set; }
    }

    public class ResponsePortInfo
    {
        public string? Name { get; set; }
        public string? ConnectedDeviceItemName { get; set; }
        public string? ConnectedPortName { get; set; }
    }

    public class ResponseNetworkInterfaceInfo : ResponseMessage
    {
        public string? ResolvedPath { get; set; }
        public string? InterfaceType { get; set; }
        public IEnumerable<ResponseNetworkNodeInfo>? Nodes { get; set; }
        public int? PortCount { get; set; }
        public IEnumerable<ResponsePortInfo>? Ports { get; set; }
        public string? IoControllerOfIoSystem { get; set; }
        public string? IoConnectorOfIoSystem { get; set; }
    }

    public class ResponseSetIpAddress : ResponseMessage
    {
        public string? ResolvedPath { get; set; }
    }

    public class ResponseConnectToSubnet : ResponseMessage
    {
        public string? ResolvedPath { get; set; }
        public string? SubnetName { get; set; }
    }

    public class ResponseDisconnectFromSubnet : ResponseMessage
    {
        public string? ResolvedPath { get; set; }
    }
}
