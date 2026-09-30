using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Application.Features.Giving;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/zakat` (11 §9.15, #115–#116; FR-S61, RUL-20).
internal sealed class AdminZakatEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/zakat").WithTags("Admin zakat").RequireAuthorization(Policies.StaffFinance);
        group.MapGet("/", LedgerAsync).Produces<ZakatLedgerResponse>();
        group.MapPost("/disbursements", VoucherAsync).Produces<ZakatEntry>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> LedgerAsync([FromServices] IQueryHandler<GetZakatLedgerQuery, ZakatLedgerResponse> handler, CancellationToken ct,
        Guid? productId = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 50) =>
        (await handler.HandleAsync(new GetZakatLedgerQuery(productId, from, to, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> VoucherAsync([FromBody] ZakatVoucherInput input,
        [FromServices] ICommandHandler<RecordZakatVoucherCommand, ZakatEntry> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RecordZakatVoucherCommand(input), ct)).ToHttp(e => TypedResults.Created("/v1/admin/zakat", e));
}
