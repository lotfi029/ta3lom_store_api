using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Dev;
using Ta3lom.Store.Application.Features.Dev.Commands.ConfirmPayment;
using Ta3lom.Store.Application.Features.Dev.Queries.GetDevSms;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Endpoints;

/// `/dev/*` — Development and Staging only (plan 14 §4.3); never mapped in Production and not part of 11.
internal sealed class DevEndpoints : IDevEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/sms", GetSmsAsync);
        app.MapPost("/orders/{reference}/confirm-payment", ConfirmPaymentAsync);                 // doc 15 §2.4
        app.MapGet("/mail", async (IDevMailInbox inbox, CancellationToken ct, int count = 20) => TypedResults.Ok(await inbox.LatestAsync(count, ct)));
        app.MapGet("/jobs", (IJobRunner jobs) => TypedResults.Ok(jobs.Jobs));
        app.MapPost("/jobs/{name}/run", RunJobAsync);
    }

    private static async Task<IResult> RunJobAsync(string name, [FromServices] IJobRunner jobs, CancellationToken ct) =>
        await jobs.TriggerAsync(name, ct) ? TypedResults.Accepted((string?)null) : TypedResults.NotFound();

    private static async Task<IResult> ConfirmPaymentAsync(
        string reference,
        [FromBody] DevConfirmPaymentRequest? request,
        [FromServices] ICommandHandler<ConfirmPaymentCommand, DevSettlementResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ConfirmPaymentCommand(reference, request?.Amount, request?.ConfirmationId), ct)).ToHttp();

    private static async Task<IResult> GetSmsAsync(
        [FromServices] IQueryHandler<GetDevSmsQuery, IReadOnlyList<DevSmsMessage>> handler,
        CancellationToken ct,
        int count = 50) =>
        (await handler.HandleAsync(new GetDevSmsQuery(count), ct)).ToHttp();
}
