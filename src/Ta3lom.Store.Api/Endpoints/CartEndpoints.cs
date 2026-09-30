using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Cart;
using Ta3lom.Store.Application.Features.Cart.Commands.AddCartLine;
using Ta3lom.Store.Application.Features.Cart.Commands.ClearCart;
using Ta3lom.Store.Application.Features.Cart.Commands.RemoveCartLine;
using Ta3lom.Store.Application.Features.Cart.Commands.UpdateCartLine;
using Ta3lom.Store.Application.Features.Cart.Queries.GetCart;
using Ta3lom.Store.Application.Features.Cart.Queries.GetCartSummary;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/cart` — server-side cart for guests (`t3s_cart`) and customers (bearer) (11 §3, #10–#15, RUL-11 … RUL-21).
/// Rate policy `cart`; Origin check on writes; `Cache-Control: no-store` (SecurityHeadersMiddleware).
internal sealed class CartEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cart").WithTags("Cart").RequireSameOrigin().RequireRateLimiting(RatePolicies.Cart)
            .RequireAuthorization(Policies.CustomerOptional);

        group.MapGet("/", GetAsync).Produces<CartResponse>();
        group.MapGet("/summary", SummaryAsync).Produces<CartBadgeResponse>();
        group.MapPost("/lines", AddLineAsync).Produces<CartResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPatch("/lines/{lineId:guid}", UpdateLineAsync).Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapDelete("/lines/{lineId:guid}", RemoveLineAsync).Produces<CartResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/", ClearAsync).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<GetCartQuery, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCartQuery(cookie.Read(http.Request)), ct)).ToHttp();

    private static async Task<IResult> SummaryAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<GetCartSummaryQuery, CartBadgeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCartSummaryQuery(cookie.Read(http.Request)), ct)).ToHttp();

    private static async Task<IResult> AddLineAsync([FromBody] AddCartLineRequest request, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<AddCartLineCommand, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new AddCartLineCommand(cookie.Read(http.Request), request), ct))
            .ToHttp(cart => Refresh(http, cookie, cart, TypedResults.Created("/v1/cart", cart)));

    private static async Task<IResult> UpdateLineAsync(Guid lineId, [FromBody] UpdateCartLineRequest request, HttpContext http,
        [FromServices] CartCookie cookie, [FromServices] ICommandHandler<UpdateCartLineCommand, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateCartLineCommand(cookie.Read(http.Request), lineId, request.Quantity), ct))
            .ToHttp(cart => Refresh(http, cookie, cart, TypedResults.Ok(cart)));

    private static async Task<IResult> RemoveLineAsync(Guid lineId, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<RemoveCartLineCommand, CartResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RemoveCartLineCommand(cookie.Read(http.Request), lineId), ct))
            .ToHttp(cart => Refresh(http, cookie, cart, TypedResults.Ok(cart)));

    private static async Task<IResult> ClearAsync(HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] ICommandHandler<ClearCartCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ClearCartCommand(cookie.Read(http.Request)), ct)).ToHttp();

    /// Guests get the cookie on first use and again on every change (30 days after the last change, RUL-16).
    private static IResult Refresh(HttpContext http, CartCookie cookie, CartResponse cart, IResult result)
    {
        if (cart is { IsGuest: true, Id: { } id })
        {
            cookie.Write(http.Response, id);
        }

        return result;
    }
}
