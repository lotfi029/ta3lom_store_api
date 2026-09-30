using Npgsql;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

public static class SqlRunner
{
    /// db/reference files copied to the test output (`reference/…`).
    public static string ReferenceFile(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference", relativePath));

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }

    /// Npgsql caches a database's types per connection string; after DDL that adds types (citext) the
    /// cache must be refreshed, or the next connection cannot send them.
    public static async Task ReloadTypesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ReloadTypesAsync();
    }

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// Runs a multi-statement script and returns, per result set, the rows rendered as text.
    public static async Task<IReadOnlyList<IReadOnlyList<string>>> QueryAllAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 300 };
        await using var reader = await command.ExecuteReaderAsync();
        var results = new List<IReadOnlyList<string>>();
        do
        {
            if (reader.FieldCount == 0)
            {
                continue;
            }

            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
            }

            results.Add(rows);
        }
        while (await reader.NextResultAsync());

        return results;
    }
}
