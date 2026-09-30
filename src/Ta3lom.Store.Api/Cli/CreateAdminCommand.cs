namespace Ta3lom.Store.Api.Cli;

/// `dotnet Ta3lom.Store.Api.dll create-admin --email …` (database/03 §3). Stub until S-05 (staff identity):
/// it runs without starting the web host and exits with code 2 so a deploy script cannot mistake it for success.
internal static class CreateAdminCommand
{
    public const string Verb = "create-admin";

    public const int NotImplementedExitCode = 2;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine("create-admin is available from session S-05 (staff identity). No user was created.");
        return NotImplementedExitCode;
    }
}
