using Ta3lom.Store.Application;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Application.Features.Staff.Commands.CreateStaff;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Infrastructure;

namespace Ta3lom.Store.Api.Cli;

/// `dotnet Ta3lom.Store.Api.dll create-admin --email … --name …` (database/03 §3, RUL-62): creates the first
/// `admin` without starting the web host, prints a one-time password once; TOTP enrolment is forced at the
/// first login (no secret yet). Audited as actor `cli:create-admin`.
internal static class CreateAdminCommand
{
    public const string Verb = "create-admin";

    public const int FailedExitCode = 1;

    public const int UsageExitCode = 2;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        var email = Option(args, "--email");
        var name = Option(args, "--name");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
        {
            output.WriteLine("usage: create-admin --email <email> --name <display name>");
            return UsageExitCode;
        }

        return RunAsync(email, name, output).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string email, string name, TextWriter output)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddInfrastructure(builder.Configuration).AddApplication().AddDomain();
        builder.Services.AddSingleton<ICurrentActor, CliActor>();

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateStaffCommand, CreateStaffResponse>>();
        var result = await handler.HandleAsync(new CreateStaffCommand(email, name, [Roles.Admin]));
        if (result.IsFailure)
        {
            await output.WriteLineAsync($"create-admin failed: {result.Error.Code} — {result.Error.Description}");
            return FailedExitCode;
        }

        await output.WriteLineAsync($"Admin {result.Value.Staff.Email} created (id {result.Value.Staff.Id}).");
        await output.WriteLineAsync($"One-time password (shown once): {result.Value.OneTimePassword}");
        await output.WriteLineAsync("TOTP enrolment is required at the first login (/v1/admin/auth/totp/enrol).");
        return 0;
    }

    private static string? Option(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// The CLI acts as a staff actor so the save interceptor writes the `audit_log` row.
    private sealed class CliActor : ICurrentActor
    {
        public ActorKind Kind => ActorKind.Staff;

        public Guid? Id => null;

        public string? Name => "cli:create-admin";

        public string? IpHash => null;
    }
}
