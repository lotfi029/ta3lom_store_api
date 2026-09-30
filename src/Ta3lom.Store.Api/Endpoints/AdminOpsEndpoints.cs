using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Application.Features.AdminOps;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// Dashboard (#53), reports (#117), message templates (#120–#122), customers (#129–#132) and the audit log (#133) — 11 §9.2,
/// §9.16, §9.18, §9.20, §9.21.
internal sealed class AdminOpsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin operations");
        admin.MapGet("/dashboard", async ([FromServices] IQueryHandler<DashboardQuery, DashboardResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new DashboardQuery(), ct)).ToHttp()).RequireAuthorization(Policies.StaffRead).Produces<DashboardResponse>();

        admin.MapGet("/reports/{name}", ReportAsync).RequireAuthorization(Policies.StaffFinance)
            .Produces<ReportResponse>().Produces(StatusCodes.Status200OK, contentType: "text/csv").ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapGet("/customers", async ([FromServices] IQueryHandler<FindCustomersQuery, IReadOnlyList<CustomerRow>> h, CancellationToken ct,
                string? mobile = null, string? email = null) => (await h.HandleAsync(new FindCustomersQuery(mobile, email), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<CustomerRow>>();
        admin.MapGet("/customers/{id:guid}", async (Guid id, [FromServices] IQueryHandler<GetCustomer360Query, Customer360> h, CancellationToken ct) =>
            (await h.HandleAsync(new GetCustomer360Query(id), ct)).ToHttp()).RequireAuthorization(Policies.StaffRead)
            .Produces<Customer360>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/customers/{id:guid}/block", async (Guid id, [FromBody] BlockCustomerRequest body,
                [FromServices] ICommandHandler<BlockCustomerCommand, CustomerRow> h, CancellationToken ct) =>
            (await h.HandleAsync(new BlockCustomerCommand(id, body.Reason ?? string.Empty), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffFinance).Produces<CustomerRow>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/customers/{id:guid}/unblock", async (Guid id, [FromServices] ICommandHandler<UnblockCustomerCommand, CustomerRow> h, CancellationToken ct) =>
            (await h.HandleAsync(new UnblockCustomerCommand(id), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffFinance).Produces<CustomerRow>().ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapGet("/templates", async ([FromServices] IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateResponse>> h, CancellationToken ct) =>
            (await h.HandleAsync(new ListTemplatesQuery(), ct)).ToHttp()).RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<TemplateResponse>>();
        admin.MapPut("/templates/{key}/{channel}/{locale}", async (string key, string channel, string locale, [FromBody] TemplateUpdateRequest body,
                [FromServices] ICommandHandler<UpdateTemplateCommand, TemplateResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new UpdateTemplateCommand(key, channel, locale, body), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffAdmin).Produces<TemplateResponse>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        admin.MapPost("/templates/{key}/{channel}/{locale}/test", async (string key, string channel, string locale, [FromBody] TemplateTestRequest body,
                [FromServices] ICommandHandler<TestTemplateCommand, TemplateTestResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new TestTemplateCommand(key, channel, locale, body), ct)).ToHttp(r => TypedResults.Accepted((string?)null, r)))
            .RequireAuthorization(Policies.StaffAdmin).Produces<TemplateTestResponse>(StatusCodes.Status202Accepted);

        admin.MapGet("/audit", async ([FromServices] IQueryHandler<AuditQuery, PagedResult<AuditEntry>> h, CancellationToken ct, string? actor = null,
                string? entity = null, string? entityId = null, string? action = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 50) =>
            (await h.HandleAsync(new AuditQuery(actor, entity, entityId, action, from, to, page, pageSize), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffAdmin).Produces<PagedResult<AuditEntry>>();
    }

    private static async Task<IResult> ReportAsync(string name, [FromServices] IQueryHandler<ReportQuery, ReportResponse> json,
        [FromServices] IQueryHandler<ReportCsvQuery, ReportFile> csv, CancellationToken ct, DateOnly? from = null, DateOnly? to = null, Guid? productId = null,
        string format = "json") =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? (await csv.HandleAsync(new ReportCsvQuery(name, from, to, productId), ct)).ToHttp(f => TypedResults.File(f.Content, "text/csv; charset=utf-8", f.FileName))
            : (await json.HandleAsync(new ReportQuery(name, from, to, productId), ct)).ToHttp();
}
