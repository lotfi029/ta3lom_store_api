using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Settings;
using Ta3lom.Store.Application.Features.Settings.Commands.UpdateSetting;
using Ta3lom.Store.Application.Features.Settings.Queries.ListSettings;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/settings` — the typed allow-list of settings (admin only). A change evicts the public catalogue cache
/// (`/v1/catalog/config` shows the minimum gift and the presets).
internal sealed class AdminSettingsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/admin/settings").WithTags("Admin · Settings")
            .RequireAuthorization(Policies.StaffAdmin).RequireRateLimiting(RatePolicies.Admin).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        settings.MapGet("/", ListAsync).Produces<IReadOnlyList<SettingResponse>>();
        settings.MapPut("/{key}", UpdateAsync).EvictsCatalogCache()
            .Produces<SettingResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync([FromServices] IQueryHandler<ListSettingsQuery, IReadOnlyList<SettingResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListSettingsQuery(), ct)).ToHttp();

    private static async Task<IResult> UpdateAsync(string key, [FromBody] SettingInput input,
        [FromServices] ICommandHandler<UpdateSettingCommand, SettingResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateSettingCommand(key, input.Value), ct)).ToHttp();
}
