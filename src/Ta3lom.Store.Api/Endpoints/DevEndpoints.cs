using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Dev.Queries.GetDevSms;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Endpoints;

/// `/dev/*` — Development and Staging only (plan 14 §4.3); never mapped in Production and not part of 11.
internal sealed class DevEndpoints : IDevEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/sms", GetSmsAsync);
    }

    private static async Task<IResult> GetSmsAsync(
        [FromServices] IQueryHandler<GetDevSmsQuery, IReadOnlyList<DevSmsMessage>> handler,
        CancellationToken ct,
        int count = 50) =>
        (await handler.HandleAsync(new GetDevSmsQuery(count), ct)).ToHttp();
}
