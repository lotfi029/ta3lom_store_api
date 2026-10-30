using Microsoft.Extensions.Configuration;
using Ta3lom.Store.Api.Extensions;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// Docker secrets: one file per setting, `__` between sections, the value without its trailing newline.
public sealed class SecretFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ta3lom-store-secrets-").FullName;

    [Fact]
    public void A_secret_file_overrides_the_same_setting_from_other_sources()
    {
        File.WriteAllText(Path.Combine(_directory, "Store__Jwt__CustomerSigningKeyBase64"), "from-file\n");
        File.WriteAllText(Path.Combine(_directory, "ConnectionStrings__Store"), "Host=db;Database=store");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Store:Jwt:CustomerSigningKeyBase64", "from-environment")])
            .AddStoreSecretFiles(_directory)
            .Build();

        configuration["Store:Jwt:CustomerSigningKeyBase64"].ShouldBe("from-file");
        configuration.GetConnectionString("Store").ShouldBe("Host=db;Database=store");
    }

    [Fact]
    public void A_missing_secrets_folder_is_ignored()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Store:Hashing:KeyBase64", "kept")])
            .AddStoreSecretFiles(Path.Combine(_directory, "missing"))
            .Build();

        configuration["Store:Hashing:KeyBase64"].ShouldBe("kept");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
