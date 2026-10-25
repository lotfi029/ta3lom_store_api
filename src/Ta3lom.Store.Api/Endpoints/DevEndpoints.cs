using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Endpoints;

/// `/dev/*` — mapped in Development only (never in Staging or Production): the SMS and e-mail sinks and running a job now.
internal sealed class DevEndpoints : IDevEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/sms", ([FromServices] IDevSmsInbox inbox, int count = 50) => TypedResults.Ok(inbox.Latest(Math.Clamp(count, 1, 200))));
        app.MapGet("/mail", async ([FromServices] IDevMailInbox inbox, CancellationToken ct, int count = 20) =>
            TypedResults.Ok(await inbox.LatestAsync(Math.Clamp(count, 1, 200), ct)));
        app.MapGet("/jobs", ([FromServices] IJobRunner jobs) => TypedResults.Ok(jobs.Jobs));
        app.MapPost("/jobs/{name}", RunJobAsync);
    }

    private static async Task<IResult> RunJobAsync(string name, [FromServices] IJobRunner jobs, CancellationToken ct) =>
        await jobs.TriggerAsync(name, ct) ? TypedResults.Accepted((string?)null) : TypedResults.NotFound();
}
