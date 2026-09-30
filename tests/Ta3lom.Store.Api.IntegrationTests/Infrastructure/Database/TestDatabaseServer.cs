using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

/// One PostgreSQL server per test run (plan 16 §2-F):
/// - default: Testcontainers `postgres:18` (CI, developer machines with Docker);
/// - `STORE_TEST_DB=external`: a running server from `STORE_TEST_DB_CONNECTION` (cloud sessions, PostgreSQL 16).
/// Every test gets throw-away databases; on PostgreSQL &lt; 18 each one receives a `public.uuidv7()` shim.
/// The shim is created here only, never by a migration.
public sealed class TestDatabaseServer
{
    private const string DefaultExternal = "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private static readonly Lazy<Task<TestDatabaseServer>> Instance = new(StartAsync);

    private readonly ConcurrentBag<string> _databases = [];

    private TestDatabaseServer(string adminConnectionString, int serverVersion, bool isExternal)
    {
        AdminConnectionString = adminConnectionString;
        ServerVersion = serverVersion;
        IsExternal = isExternal;
        if (isExternal)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DropAll();
        }
    }

    public string AdminConnectionString { get; }

    /// `server_version_num`, e.g. 160013 or 180000.
    public int ServerVersion { get; }

    public bool IsExternal { get; }

    public static Task<TestDatabaseServer> GetAsync() => Instance.Value;

    /// Creates an empty database and returns a connection string with `Search Path=store,public`.
    public async Task<string> CreateDatabaseAsync(string purpose)
    {
        var tag = purpose.Length > 16 ? purpose[..16] : purpose;
        var name = $"store_test_{tag}_{Guid.NewGuid():N}".ToLowerInvariant();   // ≤ 60 chars (PostgreSQL limit 63)
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _databases.Add(name);
        var connectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = name,
            SearchPath = "store,public",
            Pooling = false,
        }.ConnectionString;

        if (ServerVersion < 180000)
        {
            await SqlRunner.ExecuteAsync(connectionString, Uuidv7Shim);
        }

        return connectionString;
    }

    /// RFC 9562 version-7 layout (48-bit Unix ms timestamp, version and variant bits) on top of
    /// gen_random_uuid(), so ordering behaves like PostgreSQL 18's built-in uuidv7().
    public const string Uuidv7Shim = """
        CREATE FUNCTION public.uuidv7() RETURNS uuid LANGUAGE sql VOLATILE PARALLEL SAFE AS $$
          SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid())
                   PLACING substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                   FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid
        $$;
        """;

    private static async Task<TestDatabaseServer> StartAsync()
    {
        string connectionString;
        var external = string.Equals(Environment.GetEnvironmentVariable("STORE_TEST_DB"), "external", StringComparison.OrdinalIgnoreCase);
        if (external)
        {
            connectionString = Environment.GetEnvironmentVariable("STORE_TEST_DB_CONNECTION") is { Length: > 0 } cs ? cs : DefaultExternal;
        }
        else
        {
            var container = new PostgreSqlBuilder("postgres:18").Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var version = new NpgsqlCommand("SELECT current_setting('server_version_num')::int", connection);
        var serverVersion = (int)(await version.ExecuteScalarAsync())!;
        return new TestDatabaseServer(connectionString, serverVersion, external);
    }

    private void DropAll()
    {
        try
        {
            using var admin = new NpgsqlConnection(AdminConnectionString);
            admin.Open();
            foreach (var name in _databases)
            {
                using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
                drop.ExecuteNonQuery();
            }
        }
        catch (NpgsqlException)
        {
            // Best effort at process exit; leftover store_test_* databases are safe to drop by hand.
        }
    }
}
