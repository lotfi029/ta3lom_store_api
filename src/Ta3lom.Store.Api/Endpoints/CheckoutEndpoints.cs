using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Application.Features.Checkout.Commands.PlaceOrder;
using Ta3lom.Store.Application.Features.Checkout.Queries.GetOrderStatus;
using Ta3lom.Store.Application.Features.Checkout.Queries.QuoteCheckout;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/checkout` and `/v1/orders/{ref}/status` (11 §6.1–§6.3, #40–#42; RUL-15, RUL-24 … RUL-31). No payment code:
/// the order stops at `pending_payment` (`NullPaymentInitiator`, plan 14 §0).
internal sealed class CheckoutEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/checkout").WithTags("Checkout").RequireSameOrigin().RequireRateLimiting(RatePolicies.Checkout);
        checkout.MapPost("/quote", QuoteAsync).RequireAuthorization(Policies.CustomerOptional)
            .Produces<QuoteResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        checkout.MapPost("/", PlaceAsync).RequireAuthorization(Policies.Customer).RequireIdempotencyKey("checkout")
            .Produces<CheckoutResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/orders/{reference}/status", StatusAsync).WithTags("Checkout").RequireAuthorization(Policies.CustomerOptional)
            .RequireRateLimiting(RatePolicies.Public).Produces<OrderStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> QuoteAsync([FromBody] QuoteRequest? request, HttpContext http, [FromServices] CartCookie cookie,
        [FromServices] IQueryHandler<QuoteCheckoutQuery, QuoteResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new QuoteCheckoutQuery(cookie.Read(http.Request), request?.ShippingAddressId), ct)).ToHttp();

    private static async Task<IResult> PlaceAsync([FromBody] CheckoutRequest request,
        [FromServices] ICommandHandler<PlaceOrderCommand, CheckoutResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new PlaceOrderCommand(request), ct))
            .ToHttp(order => TypedResults.Created($"/v1/orders/{order.Reference}/status", order));

    private static async Task<IResult> StatusAsync(string reference, [FromQuery(Name = "t")] string? statusToken,
        [FromServices] IQueryHandler<GetOrderStatusQuery, OrderStatusResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetOrderStatusQuery(reference, statusToken), ct)).ToHttp();
}
