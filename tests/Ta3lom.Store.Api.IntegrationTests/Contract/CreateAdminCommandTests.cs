using Ta3lom.Store.Api.Cli;

namespace Ta3lom.Store.Api.IntegrationTests.Contract;

/// `create-admin` exists as a verb (database/03 §3) and refuses to run without its required options (S-05).
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
}
