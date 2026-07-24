using System.Net;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.AgentGateway;

public sealed class RequestNetworkContext : IRequestNetworkContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestNetworkContext(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public IPAddress? ClientIp
    {
        get
        {
            var address = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            return address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
        }
    }
}
