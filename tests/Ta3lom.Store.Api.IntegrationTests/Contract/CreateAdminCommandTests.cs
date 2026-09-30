using Ta3lom.Store.Api.Cli;

namespace Ta3lom.Store.Api.IntegrationTests.Contract;

/// `create-admin` exists as a verb (database/03 §3) and refuses to pretend success until S-05.
public sealed class CreateAdminCommandTests
{
    [Fact]
    public void Stub_explains_itself_and_exits_with_a_failure_code()
    {
        using var output = new StringWriter();

        var exitCode = CreateAdminCommand.Run(["--email", "admin@ta3lom.sa"], output);

        exitCode.ShouldBe(CreateAdminCommand.NotImplementedExitCode);
        output.ToString().ShouldContain("S-05");
    }
}
