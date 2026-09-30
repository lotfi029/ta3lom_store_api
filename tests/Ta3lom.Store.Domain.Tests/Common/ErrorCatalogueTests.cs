using System.Reflection;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

/// The per-aggregate error catalogues build `Error`s whose code is one of the 11 §1.6 codes.
public sealed class ErrorCatalogueTests
{
    private static readonly HashSet<string> Codes = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);

    private static void ShouldBeCatalogued(Error error, int status)
    {
        Codes.ShouldContain(error.Code);
        error.Status.ShouldBe(status);
        error.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_parameterless_catalogue_error_uses_a_known_code_and_an_http_error_status()
    {
        var catalogues = typeof(ErrorCodes).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ErrorCodes).Namespace && t is { IsAbstract: true, IsSealed: true } && t != typeof(ErrorCodes)).ToList();
        catalogues.Count.ShouldBeGreaterThan(10);

        var errors = catalogues.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
            .Where(p => p.PropertyType == typeof(Error)).Select(p => (Name: $"{p.DeclaringType!.Name}.{p.Name}", Error: (Error)p.GetValue(null)!)).ToList();

        errors.Count.ShouldBeGreaterThan(50);
        foreach (var (name, error) in errors)
        {
            Codes.ShouldContain(error.Code, name);
            error.Status.ShouldBeInRange(400, 599, name);
            error.Description.ShouldNotBeNullOrWhiteSpace(name);
        }
    }

    [Fact]
    public void Parameterised_errors_carry_their_problem_members()
    {
        var lockedUntil = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        var formatId = Guid.CreateVersion7();
        IReadOnlyList<string> missing = ["titleAr"];
        IReadOnlyList<AllocationRejection> rejections = [new("B-000001", ErrorCodes.AllocationNotApproved)];

        var cases = new (Error Error, int Status, string Key, object? Value)[]
        {
            (AdminCatalogErrors.PublishIncomplete(missing), 422, "missing", missing),
            (AdminCatalogErrors.MediaTooLarge(5_000_000), 413, "max", 5_000_000L),
            (AllocationErrors.Rejected(rejections), 422, "errors", rejections),
            (BeneficiaryErrors.ImportInvalid("report"), 422, "report", "report"),
            (CheckoutErrors.LinesInvalid("lines"), 422, "lines", "lines"),
            (CheckoutErrors.PriceChanged("quote"), 409, "quote", "quote"),
            (CheckoutErrors.OutOfStock(formatId), 409, "bookFormatId", formatId),
            (CheckoutErrors.GatewayUnavailable("T3-26-000001", "tok"), 503, "statusToken", "tok"),
            (GivingErrors.ZakatBalanceInsufficient(new Money(12.5m)), 422, "balance", "12.50"),
            (GivingErrors.TemplateRenderFailed("bad", 3, 7), 422, "column", 7),
            (OtpErrors.RateLimited(TimeSpan.FromSeconds(12.2)), 429, "retryAfterSeconds", 13),
            (OtpErrors.RateLimited(TimeSpan.Zero), 429, "retryAfterSeconds", 1),
            (OtpErrors.Invalid(3), 400, "attemptsLeft", 3),
            (StaffErrors.AccountLocked(lockedUntil), 423, "lockedUntil", lockedUntil),
        };

        foreach (var (error, status, key, value) in cases)
        {
            ShouldBeCatalogued(error, status);
            error.Extensions.ShouldNotBeNull();
            error.Extensions[key].ShouldBe(value, error.Code);
        }

        CheckoutErrors.GatewayUnavailable("T3-26-000001", "tok").Extensions!["reference"].ShouldBe("T3-26-000001");
        GivingErrors.TemplateRenderFailed("bad", 3, 7).Extensions!["line"].ShouldBe(3);
        GivingErrors.TemplateRenderFailed("bad", 3, 7).Description.ShouldBe("bad");
    }

    [Fact]
    public void Idempotency_errors_follow_the_checkout_contract()
    {
        ShouldBeCatalogued(IdempotencyErrors.KeyRequired, 400);
        ShouldBeCatalogued(IdempotencyErrors.KeyReused, 422);
        ShouldBeCatalogued(IdempotencyErrors.InProgress, 409);
    }

    [Fact]
    [Rule("RUL-62")]
    public void Staff_lockout_and_blocked_customers_have_their_own_statuses()
    {
        ShouldBeCatalogued(AuthErrors.AccountBlocked, 403);
        ShouldBeCatalogued(StaffErrors.InvalidCredentials, 401);
        ShouldBeCatalogued(AllocationErrors.UnitInvalidTransition, 409);
        ShouldBeCatalogued(SettlementErrors.CancelNotAllowed, 409);
    }
}
