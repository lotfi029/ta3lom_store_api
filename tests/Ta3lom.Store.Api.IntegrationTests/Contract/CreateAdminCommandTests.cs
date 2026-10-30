using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Api.Cli;

namespace Ta3lom.Store.Api.IntegrationTests.Contract;

/// `create-admin` exists as a verb and refuses to run without its required options.
public sealed class CreateAdminCommandTests
{
    [Fact]
    public void Missing_options_print_usage_and_exit_with_a_failure_code()
    {
        using var output = new StringWriter();

        var exitCode = CreateAdminCommand.Run(["--email", "admin@ta3lom.sa"], output);

        exitCode.ShouldBe(CreateAdminCommand.UsageExitCode);
        output.ToString().ShouldContain("--name");
    }

    /// Development validates every service at build time: the CLI host must register what the API layer adds for the
    /// Application handlers (web links, OTP limits), or `create-admin` crashes before it runs.
    [Fact]
    public void The_cli_host_builds_with_development_service_validation()
    {
        using var host = CreateAdminCommand.BuildHost(Environments.Development);

        host.ShouldNotBeNull();
    }
}
