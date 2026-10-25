using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Application.Features.AdminOps;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/{dashboard,customers,orders,templates,audit}` (BR-A12, BR-A13). Reads for every staff role unless noted;
/// donors are blocked by admin and store_manager, orders cancelled by admin and finance, templates edited by admin and
/// store_manager, the audit log is admin only.
internal sealed class AdminOpsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireRateLimiting(RatePolicies.Admin).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapGet("/dashboard", DashboardAsync).WithTags("Admin · Dashboard").RequireAuthorization(Policies.StaffRead).Produces<DashboardResponse>();

        var customers = admin.MapGroup("/customers").WithTags("Admin · Donors");
        customers.MapGet("/", ListCustomersAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<PagedResult<AdminCustomerRow>>().ProducesProblem(StatusCodes.Status400BadRequest);
        customers.MapGet("/{id:guid}", GetCustomerAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<AdminCustomerDetail>().ProducesProblem(StatusCodes.Status404NotFound);
        customers.MapPost("/{id:guid}/block", BlockAsync).RequireAuthorization(Policies.StaffCustomers)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        customers.MapPost("/{id:guid}/unblock", UnblockAsync).RequireAuthorization(Policies.StaffCustomers)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        var orders = admin.MapGroup("/orders").WithTags("Admin · Orders");
        orders.MapGet("/", ListOrdersAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<PagedResult<AdminOrderRow>>().ProducesProblem(StatusCodes.Status400BadRequest);
        orders.MapGet("/{reference}", GetOrderAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<AdminOrderDetail>().ProducesProblem(StatusCodes.Status404NotFound);
        orders.MapPost("/{reference}/cancel", CancelAsync).RequireAuthorization(Policies.StaffOrders)
            .Produces<AdminOrderRow>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        var templates = admin.MapGroup("/templates").WithTags("Admin · Templates").RequireAuthorization(Policies.StaffTemplates);
        templates.MapGet("/", ListTemplatesAsync).Produces<IReadOnlyList<TemplateResponse>>();
        templates.MapPost("/test", TestTemplateAsync).RequireRateLimiting(RatePolicies.TemplateTest)   // D-b: 5 per hour per staff member
            .Produces<TemplateTestResponse>().ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        templates.MapPut("/{key}/{channel}/{locale}", UpdateTemplateAsync).Produces<TemplateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        admin.MapGet("/audit", AuditAsync).WithTags("Admin · Audit").RequireAuthorization(Policies.StaffAdmin)
            .Produces<PagedResult<AuditRow>>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> DashboardAsync([FromServices] IQueryHandler<GetDashboardQuery, DashboardResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetDashboardQuery(), ct)).ToHttp();

    private static async Task<IResult> ListCustomersAsync([FromServices] IQueryHandler<ListCustomersQuery, PagedResult<AdminCustomerRow>> handler,
        CancellationToken ct, string? q = null, string? status = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListCustomersQuery(q, status, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> GetCustomerAsync(Guid id, [FromServices] IQueryHandler<GetCustomerQuery, AdminCustomerDetail> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCustomerQuery(id), ct)).ToHttp();

    private static async Task<IResult> BlockAsync(Guid id, [FromServices] ICommandHandler<BlockCustomerCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new BlockCustomerCommand(id), ct)).ToHttp(() => TypedResults.NoContent());

    private static async Task<IResult> UnblockAsync(Guid id, [FromServices] ICommandHandler<UnblockCustomerCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UnblockCustomerCommand(id), ct)).ToHttp(() => TypedResults.NoContent());

    private static async Task<IResult> ListOrdersAsync([FromServices] IQueryHandler<ListOrdersQuery, PagedResult<AdminOrderRow>> handler, CancellationToken ct,
        string? reference = null, string? mobile = null, string? status = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListOrdersQuery(reference, mobile, status, from, to, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> GetOrderAsync(string reference, [FromServices] IQueryHandler<GetAdminOrderQuery, AdminOrderDetail> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new GetAdminOrderQuery(reference), ct)).ToHttp();

    private static async Task<IResult> CancelAsync(string reference, [FromServices] ICommandHandler<CancelOrderCommand, AdminOrderRow> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CancelOrderCommand(reference), ct)).ToHttp();

    private static async Task<IResult> ListTemplatesAsync([FromServices] IQueryHandler<ListTemplatesQuery, IReadOnlyList<TemplateResponse>> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListTemplatesQuery(), ct)).ToHttp();

    private static async Task<IResult> UpdateTemplateAsync(string key, string channel, string locale, [FromBody] TemplateInput input,
        [FromServices] ICommandHandler<UpdateTemplateCommand, TemplateResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateTemplateCommand(key, channel, locale, input), ct)).ToHttp();

    private static async Task<IResult> TestTemplateAsync([FromBody] TemplateTestRequest request,
        [FromServices] ICommandHandler<TestTemplateCommand, TemplateTestResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new TestTemplateCommand(request), ct)).ToHttp();

    private static async Task<IResult> AuditAsync([FromServices] IQueryHandler<ListAuditQuery, PagedResult<AuditRow>> handler, CancellationToken ct,
        string? actor = null, string? action = null, string? entity = null, string? entityId = null, int page = 1, int pageSize = 50) =>
        (await handler.HandleAsync(new ListAuditQuery(actor, action, entity, entityId, page, pageSize), ct)).ToHttp();
}
