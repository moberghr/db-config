using DbConfig.Core;
using Microsoft.AspNetCore.Http;

namespace DbConfig.Http.Endpoints;

internal static class ReloadEndpoint
{
    internal static IResult Handle(IDbConfigReloadSignal reloadSignal)
    {
        reloadSignal.Trigger();

        return Results.NoContent();
    }
}
