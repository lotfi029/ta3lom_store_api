using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Application.Features.Beneficiaries.Commands;
using Ta3lom.Store.Application.Features.Beneficiaries.Queries;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/beneficiaries` (11 §9.13, #100–#108; RUL-51, RUL-52, RUL-62, PDPL): masked everywhere except the audited reveal.
internal sealed class AdminBeneficiaryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/beneficiaries").WithTags("Admin beneficiaries").RequireAuthorization(Policies.StaffBeneficiaries);

        group.MapGet("/", ListAsync).Produces<PagedResult<BeneficiaryRow>>();
        group.MapPost("/", CreateAsync).Produces<BeneficiaryRow>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/import/template", TemplateAsync).Produces(StatusCodes.Status200OK, contentType: "text/csv");
        group.MapPost("/import", ImportAsync).DisableAntiforgery().Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportReport>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/{id:guid}", GetAsync).Produces<BeneficiaryDetail>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:guid}", UpdateAsync).Produces<BeneficiaryRow>().ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/eligibility", EligibilityAsync).Produces<BeneficiaryRow>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/reveal", RevealAsync).RequireAuthorization(Policies.StaffRevealPii)
            .Produces<BeneficiaryPii>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/invite", InviteAsync).Produces(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync([FromServices] IQueryHandler<ListBeneficiariesQuery, PagedResult<BeneficiaryRow>> handler, CancellationToken ct,
        string? group = null, string? status = null, bool? zakat = null, string? region = null, string? kind = null, string? q = null,
        int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListBeneficiariesQuery(group, status, zakat, region, kind, q, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> CreateAsync([FromBody] BeneficiaryInput input,
        [FromServices] ICommandHandler<CreateBeneficiaryCommand, BeneficiaryRow> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateBeneficiaryCommand(input), ct)).ToHttp(b => TypedResults.Created($"/v1/admin/beneficiaries/{b.Id}", b));

    private static async Task<IResult> GetAsync(Guid id, [FromServices] IQueryHandler<GetBeneficiaryQuery, BeneficiaryDetail> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetBeneficiaryQuery(id), ct)).ToHttp();

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] BeneficiaryInput input,
        [FromServices] ICommandHandler<UpdateBeneficiaryCommand, BeneficiaryRow> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateBeneficiaryCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> EligibilityAsync(Guid id, [FromBody] EligibilityInput input,
        [FromServices] ICommandHandler<SetEligibilityCommand, BeneficiaryRow> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new SetEligibilityCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> RevealAsync(Guid id, [FromBody] RevealRequest request,
        [FromServices] ICommandHandler<RevealBeneficiaryCommand, BeneficiaryPii> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RevealBeneficiaryCommand(id, request.Reason ?? string.Empty), ct)).ToHttp();

    private static async Task<IResult> InviteAsync(Guid id, [FromServices] ICommandHandler<InviteBeneficiaryCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new InviteBeneficiaryCommand(id), ct)).ToHttp(() => TypedResults.Accepted((string?)null));

    private static async Task<IResult> ImportAsync(IFormFile file, [FromForm] bool? dryRun,
        [FromServices] ICommandHandler<ImportBeneficiariesCommand, ImportReport> handler, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        return (await handler.HandleAsync(new ImportBeneficiariesCommand(content, file.Length, dryRun ?? true), ct)).ToHttp();
    }

    private static async Task<IResult> TemplateAsync([FromServices] IQueryHandler<BeneficiaryTemplateQuery, byte[]> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new BeneficiaryTemplateQuery(), ct)).ToHttp(b => TypedResults.File(b, "text/csv; charset=utf-8", "beneficiaries-template.csv"));
}
