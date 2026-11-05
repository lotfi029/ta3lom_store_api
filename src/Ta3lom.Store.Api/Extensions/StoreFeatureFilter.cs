using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Extensions;

/// A store feature that configuration can switch off (showcase mode).
public enum StoreFeature
{
    /// `Store:Payments:Provider` ≠ `none`: cart lines, checkout, pay, finance actions.
    Payments,

    /// `Store:Features:CustomerSignIn`: the donor OTP routes.
    CustomerSignIn,
}

/// Endpoint metadata naming the feature a route needs (read by the showcase policy test).
public sealed record StoreFeatureMetadata(StoreFeature Feature);

/// Answers 409 `store.payments_disabled` / `store.signin_disabled` when the route's feature is off. Added BEFORE the
/// idempotency filter, so a refused request reaches no handler and stores no idempotency record (a later retry with the
/// same key, after the switch-on, is a fresh request).
internal sealed class StoreFeatureFilter(StoreFeature feature) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var mode = context.HttpContext.RequestServices.GetRequiredService<IStoreMode>();
        return feature switch
        {
            StoreFeature.Payments when !mode.PaymentsEnabled => ValueTask.FromResult<object?>(StoreErrors.PaymentsDisabled.ToProblem()),
            StoreFeature.CustomerSignIn when !mode.CustomerSignIn => ValueTask.FromResult<object?>(StoreErrors.SignInDisabled.ToProblem()),
            _ => next(context),
        };
    }
}

public static class StoreFeatureExtensions
{
    /// The route needs `feature`; off → 409 before any other filter that writes (idempotency). Call it before
    /// `RequireIdempotencyKey`.
    public static RouteHandlerBuilder RequireStoreFeature(this RouteHandlerBuilder builder, StoreFeature feature) =>
        builder.WithMetadata(new StoreFeatureMetadata(feature)).AddEndpointFilter(new StoreFeatureFilter(feature))
            .ProducesProblem(StatusCodes.Status409Conflict);
}
