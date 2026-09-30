using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure;

/// TestServer has no socket, so tests choose the peer address with `X-Test-Remote-Ip`
/// (default: an unknown public address). Runs before Program's pipeline.
public sealed class RemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            var requested = context.Request.Headers[Header].ToString();
            context.Connection.RemoteIpAddress = IPAddress.Parse(requested.Length > 0 ? requested : StoreApiFactory.UnknownClientIp);
            context.Request.Headers.Remove(Header);
            return nextMiddleware(context);
        });
        next(app);
    };
}
