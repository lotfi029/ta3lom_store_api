using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Application.Abstractions.Behaviours;

namespace Ta3lom.Store.Application.Tests.Abstractions;

public sealed class DecoratorTests
{
    private const string SecretMobile = "0551234567";

    [Fact]
    public async Task Validation_failure_returns_400_shape_and_skips_the_handler()
    {
        var inner = new RegisterMobileHandler();
        var sut = new ValidatingCommandHandler<RegisterMobile, Unit>(inner, [new RegisterMobileValidator()]);

        var result = await sut.HandleAsync(new RegisterMobile("12345", new AddressInput("")), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.ValidationFailed);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        var errors = (IReadOnlyDictionary<string, string[]>)result.Error.Extensions!["errors"]!;
        errors.Keys.ShouldBe(["mobileNumber", "address.cityName"], ignoreOrder: true);
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        var inner = new RegisterMobileHandler();
        var sut = new ValidatingCommandHandler<RegisterMobile, Unit>(inner, [new RegisterMobileValidator()]);

        var result = await sut.HandleAsync(new RegisterMobile(SecretMobile, new AddressInput("الرياض")), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Logging_decorator_logs_name_and_outcome_but_never_the_payload()
    {
        var logger = new RecordingLogger<LoggingCommandHandler<RegisterMobile, Unit>>();
        var inner = new RegisterMobileHandler();
        var sut = new LoggingCommandHandler<RegisterMobile, Unit>(inner, logger);
        var command = new RegisterMobile(SecretMobile, new AddressInput("الرياض"));

        await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        inner.FailWith = new Error("otp.invalid", "Wrong code", ErrorKind.Validation);
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        logger.Entries.Count.ShouldBe(2);
        logger.Entries[0].Message.ShouldContain(nameof(RegisterMobile));
        logger.Entries[1].Message.ShouldContain("otp.invalid");
        foreach (var entry in logger.Entries)
        {
            entry.Message.ShouldNotContain(SecretMobile);
            entry.Message.ShouldNotContain("الرياض");
            entry.State.ShouldAllBe(kv => !(kv.Value ?? "").ToString()!.Contains(SecretMobile, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AddApplication_wraps_command_handlers_logging_then_validation()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<ICommandHandler<RegisterMobile, Unit>, RegisterMobileHandler>();
        services.AddScoped<IValidator<RegisterMobile>, RegisterMobileValidator>();

        services.AddApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterMobile, Unit>>();
        handler.ShouldBeOfType<LoggingCommandHandler<RegisterMobile, Unit>>();
    }

    [Fact]
    public void AddApplication_succeeds_while_no_handler_exists()
    {
        var services = new ServiceCollection().AddLogging();

        Should.NotThrow(() => services.AddApplication());
    }

    [Fact]
    public async Task Decorated_handler_still_validates()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<ICommandHandler<RegisterMobile, Unit>, RegisterMobileHandler>();
        services.AddScoped<IValidator<RegisterMobile>, RegisterMobileValidator>();
        services.AddApplication();
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandHandler<RegisterMobile, Unit>>()
            .HandleAsync(new RegisterMobile("bad", new AddressInput("x")), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe(ErrorCodes.ValidationFailed);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(string Message, IReadOnlyList<KeyValuePair<string, object?>> State)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add((formatter(state, exception), values));
        }
    }
}
