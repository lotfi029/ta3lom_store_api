namespace Ta3lom.Store.Api.Extensions;

/// Docker secrets: one file per setting under `/run/secrets` (or `STORE_SECRETS_DIR`), the file name is the key with `__`
/// between sections (`Store__Jwt__CustomerSigningKeyBase64`) and the content is the value. Added last, so a secret file
/// wins over appsettings and environment variables. A missing folder is fine (Development, tests).
public static class SecretFilesSetup
{
    public const string DefaultDirectory = "/run/secrets";

    public const string DirectoryVariable = "STORE_SECRETS_DIR";

    public static IConfigurationBuilder AddStoreSecretFiles(this IConfigurationBuilder configuration, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        directory ??= Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment : DefaultDirectory;
        return configuration.AddKeyPerFile(directory, optional: true, reloadOnChange: false);
    }
}
