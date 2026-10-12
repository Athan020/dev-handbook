using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Assessments.Set3.SqlFixture))]

namespace Assessments.Set3;

// Starts Postgres once for the whole set, with two databases: the visible sample data and
// the hidden data the questions are scored against.
public sealed class SqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var schema = await File.ReadAllTextAsync(DataFile("schema.sql"));
        foreach (var dataset in new[] { "visible", "hidden" })
        {
            await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
            {
                await admin.OpenAsync();
                await new NpgsqlCommand($"CREATE DATABASE {dataset}", admin).ExecuteNonQueryAsync();
            }
            await using var connection = await OpenAsync(dataset);
            await new NpgsqlCommand(schema + await File.ReadAllTextAsync(DataFile($"{dataset}.sql")), connection).ExecuteNonQueryAsync();
        }
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    // Runs a query file against a dataset, and returns each row as text, the way a platform
    // compares results: NULL as "NULL", dates as yyyy-MM-dd, timestamps as yyyy-MM-dd HH:mm:ss.
    public async Task<List<string>> RunAsync(string query, string dataset)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Queries", $"{query}.sql");
        var sql = await File.ReadAllTextAsync(path);
        var hasQuery = sql.Split('\n').Any(line => line.Trim() is { Length: > 0 } text && !text.StartsWith("--", StringComparison.Ordinal));
        Assert.True(hasQuery, $"Write your query in Problems/{query}.sql.");

        await using var connection = await OpenAsync(dataset);
        await using var reader = await new NpgsqlCommand(sql, connection).ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i) switch
                {
                    DateTime timestamp => timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                    var value => value.ToString() ?? "",
                };
            }
            rows.Add(string.Join(" | ", values));
        }
        return rows;
    }

    private async Task<NpgsqlConnection> OpenAsync(string database)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
}
