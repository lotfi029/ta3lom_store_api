using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Application.Features.Donations;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/cart`, `/v1/checkout`, `/v1/orders/{reference}/status` (BR-A07, BR-A08): a guest uses the `t3s_cart` cookie, a
/// signed-in donor their own cart.
internal sealed class DonationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/cart").WithTags("Cart").RequireAuthorization(Policies.CustomerOptional)
            .RequireRateLimiting(RatePolicies.Cart).RequireSameOrigin().ProducesProblem(StatusCodes.Status401Unauthorized);
        cart.MapGet("/", GetCartAsync).Produces<CartResponse>();
        cart.MapGet("/summary", GetSummaryAsync).Produces<CartSummaryResponse>();
        cart.MapPost("/lines", AddLineAsync).Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        cart.MapPatch("/lines/{id:guid}", UpdateLineAsync).Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        cart.MapDelete("/lines/{id:guid}", RemoveLineAsync).Produces<CartResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        cart.MapDelete("/", ClearAsync).Produces(StatusCodes.Status204NoContent);

        var checkout = app.MapGroup("/checkout").WithTags("Checkout").RequireSameOrigin().ProducesProblem(StatusCodes.Status401Unauthorized);
        checkout.MapPost("/quote", QuoteAsync).RequireAuthorization(Policies.CustomerOptional).RequireRateLimiting(RatePolicies.Cart)
            .Produces<CartResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        checkout.MapPost("/", CheckoutAsync).RequireAuthorization(Policies.Customer).RequireRateLimiting(RatePolicies.Checkout)
            .RequireIdempotencyKey("checkout", CheckoutReplay.Instance)
            .Produces<CheckoutResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/orders/{reference}/status", OrderStatusAsync).WithTags("Orders").RequireAuthorization(Policies.CustomerOptional)
            .RequireRateLimiting(RatePolicies.Public).Produces<OrderStatusResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static CartOwner Owner(HttpContext http, CartCookie cookie) =>
        http.User.Identity?.IsAuthenticated == true ? new CartOwner(http.User.SubjectId(), null) : new CartOwner(null, cookie.Read(http.Request));

    private static Guid? CustomerId(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? user.SubjectId() : null;

    private static IResult WithCookie(Result<CartResult> result, HttpContext http, CartCookie cookie) =>
        result.ToHttp(r =>
        {
            if (r.NewGuestCartId is { } id)
            {
                cookie.Write(http.Response, id);
            }

            return TypedResults.Ok(r.Cart);
        });

    private static async Task<IResult> GetCartAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<GetCartQuery, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCartQuery(Owner(http, cookie)), ct)).ToHttp();

    private static async Task<IResult> GetSummaryAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<GetCartSummaryQuery, CartSummaryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCartSummaryQuery(Owner(http, cookie)), ct)).ToHttp();

    private static async Task<IResult> AddLineAsync([FromBody] AddCartLineRequest request, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<AddCartLineCommand, CartResult> handler, CancellationToken ct) =>
        WithCookie(await handler.HandleAsync(new AddCartLineCommand(Owner(http, cookie), request), ct), http, cookie);

    private static async Task<IResult> UpdateLineAsync(Guid id, [FromBody] UpdateCartLineRequest request, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<UpdateCartLineCommand, CartResult> handler, CancellationToken ct) =>
        WithCookie(await handler.HandleAsync(new UpdateCartLineCommand(Owner(http, cookie), id, request), ct), http, cookie);

    private static async Task<IResult> RemoveLineAsync(Guid id, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<RemoveCartLineCommand, CartResult> handler, CancellationToken ct) =>
        WithCookie(await handler.HandleAsync(new RemoveCartLineCommand(Owner(http, cookie), id), ct), http, cookie);

    private static async Task<IResult> ClearAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<ClearCartCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ClearCartCommand(Owner(http, cookie)), ct)).ToHttp(() => TypedResults.NoContent());

    private static async Task<IResult> QuoteAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<QuoteCheckoutQuery, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new QuoteCheckoutQuery(Owner(http, cookie)), ct)).ToHttp();

    private static async Task<IResult> CheckoutAsync([FromBody] CheckoutRequest request, ClaimsPrincipal user,
        [FromServices] ICommandHandler<CheckoutCommand, CheckoutResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CheckoutCommand(user.SubjectId(), request), ct))
            .ToHttp(o => TypedResults.Created($"/v1/orders/{o.Reference}/status", o));

    private static async Task<IResult> OrderStatusAsync(string reference, ClaimsPrincipal user,
        [FromServices] IQueryHandler<GetOrderStatusQuery, OrderStatusResponse> handler, CancellationToken ct, string? t = null) =>
        (await handler.HandleAsync(new GetOrderStatusQuery(reference, t, CustomerId(user)), ct)).ToHttp();
}

/// `/v1/me` (BR-A10, D-13, D-14): the signed-in donor's profile, export, deletion and donation orders.
internal sealed class MeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").WithTags("Me").RequireAuthorization(Policies.Customer).RequireRateLimiting(RatePolicies.Me).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        me.MapGet("/", GetAsync).Produces<MeResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPatch("/", UpdateAsync).Produces<MeResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);
        me.MapDelete("/", DeleteAsync).Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden);
        me.MapGet("/export", ExportAsync).Produces<MeExportResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        me.MapGet("/donations", DonationsAsync).Produces<PagedResult<DonationSummary>>().ProducesProblem(StatusCodes.Status400BadRequest);
        me.MapGet("/donations/{reference}", DonationAsync).Produces<DonationDetail>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, [FromServices] IQueryHandler<GetMeQuery, MeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetMeQuery(user.SubjectId()), ct)).ToHttp();

    private static async Task<IResult> UpdateAsync([FromBody] UpdateMeRequest request, ClaimsPrincipal user,
        [FromServices] ICommandHandler<UpdateMeCommand, MeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateMeCommand(user.SubjectId(), request), ct)).ToHttp();

    private static async Task<IResult> DeleteAsync(HttpContext http, [FromServices] ICommandHandler<DeleteMeCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeleteMeCommand(http.User.SubjectId()), ct)).ToHttp(() =>
        {
            AuthCookies.Clear(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath);
            CartCookie.Delete(http.Response);
            return TypedResults.NoContent();
        });

    private static async Task<IResult> ExportAsync(ClaimsPrincipal user, [FromServices] IQueryHandler<ExportMeQuery, MeExportResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ExportMeQuery(user.SubjectId()), ct)).ToHttp();

    private static async Task<IResult> DonationsAsync(ClaimsPrincipal user, [FromServices] IQueryHandler<ListMyDonationsQuery, PagedResult<DonationSummary>> handler,
        CancellationToken ct, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListMyDonationsQuery(user.SubjectId(), page, pageSize), ct)).ToHttp();

    private static async Task<IResult> DonationAsync(string reference, ClaimsPrincipal user, [FromServices] IQueryHandler<GetMyDonationQuery, DonationDetail> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new GetMyDonationQuery(user.SubjectId(), reference), ct)).ToHttp();
}

/// L-1: the stored checkout replay holds no status token; it is re-derived from the reference (a deterministic HMAC) when
/// the response is replayed.
internal sealed class CheckoutReplay : IIdempotentResponseShaper
{
    public static readonly CheckoutReplay Instance = new();

    public object ForStorage(object value) => value is CheckoutResponse r ? r with { StatusToken = string.Empty } : value;

    public string ForReplay(string storedJson, System.Text.Json.JsonSerializerOptions json, IServiceProvider services)
    {
        var stored = System.Text.Json.JsonSerializer.Deserialize<CheckoutResponse>(storedJson, json)!;
        var token = services.GetRequiredService<Ta3lom.Store.Application.Interfaces.IOrderStatusTokens>().Create(stored.Reference).Token;
        return System.Text.Json.JsonSerializer.Serialize(stored with { StatusToken = token }, json);
    }
}
