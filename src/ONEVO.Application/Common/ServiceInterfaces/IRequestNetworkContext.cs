using System.Net;

namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IRequestNetworkContext
{
    IPAddress? ClientIp { get; }
}
