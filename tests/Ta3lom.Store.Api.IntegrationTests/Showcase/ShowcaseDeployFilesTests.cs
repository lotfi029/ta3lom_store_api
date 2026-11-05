namespace Ta3lom.Store.Api.IntegrationTests.Showcase;

/// SM-7: the deploy files switch showcase mode on by environment variables only — compose defaults stay live (moyasar,
/// sign-in on), both env examples carry the two lines, stack-check has a showcase run, and the runbooks say which secret
/// files showcase does not need. The stack itself runs in CI (job `stack`, both modes).
public sealed class ShowcaseDeployFilesTests
{
    private static readonly string Deploy = Path.Combine(RepositoryRoot(), "deploy");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Deploy, file));

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ta3lom.Store.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Ta3lom.Store.slnx not found above the test output");
    }

    [Fact]
    public void Compose_reads_the_provider_and_the_sign_in_switch_from_the_env_file_with_live_defaults()
    {
        var compose = Read("compose.yaml");

        compose.ShouldContain("Store__Payments__Provider: ${PAYMENTS_PROVIDER:-moyasar}");
        compose.ShouldContain("Store__Features__CustomerSignIn: ${CUSTOMER_SIGNIN:-true}");
        compose.ShouldNotContain("Store__Payments__Provider: moyasar");
    }

    [Theory]
    [InlineData(".env.production.example")]
    [InlineData(".env.staging.example")]
    public void Both_env_examples_carry_the_two_switches(string file)
    {
        var lines = Read(file).Split('\n');

        lines.ShouldContain(l => l.StartsWith("PAYMENTS_PROVIDER=moyasar", StringComparison.Ordinal) && l.Contains("showcase: none", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("CUSTOMER_SIGNIN=true", StringComparison.Ordinal) && l.Contains("showcase: false", StringComparison.Ordinal));
    }

    [Fact]
    public void Stack_check_has_a_showcase_run_without_Moyasar_or_SMS_secret_files()
    {
        var script = Read("stack-check.sh");

        script.ShouldContain("STACK_MODE");
        script.ShouldContain("\"mode\":\"showcase\"");
        script.ShouldContain("PAYMENTS_PROVIDER=none");
    }

    [Fact]
    public void The_runbooks_document_showcase_mode()
    {
        var secrets = Read("secrets.md");
        var readme = Read("README.md");

        secrets.ShouldContain("## Showcase");
        foreach (var file in new[] { "Store__Payments__Moyasar__SecretKey", "Store__Payments__Moyasar__WebhookSecret", "Store__Sms__AppSid" })
        {
            secrets[secrets.IndexOf("## Showcase", StringComparison.Ordinal)..].ShouldContain(file);
        }

        readme.ShouldContain("## Showcase mode");
        readme.ShouldContain("PAYMENTS_PROVIDER=none");
        readme.ShouldContain("CUSTOMER_SIGNIN=false");
    }
}
