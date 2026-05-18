using System.Net;
using Microsoft.AspNetCore.Http;

namespace DbConfig.Ui;

/// <summary>
/// Authorization filter that allows access only from loopback addresses
/// (127.0.0.1 / ::1). Convenient for local development and demos; do not
/// use it as the sole defense for a production deployment.
/// </summary>
public sealed class LocalRequestsOnlyAuthorizationFilter : IDbConfigAuthorizationFilter
{
    public Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        var allowed = remoteIp is null || IPAddress.IsLoopback(remoteIp);

        return Task.FromResult(allowed);
    }
}
