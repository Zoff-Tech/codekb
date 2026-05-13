using System.Reflection;
using Npgsql;

namespace CodeKb.Storage.Postgres.Migrations;

public sealed record Migration(string Name, string Sql);

public sealed class MigrationRunner
{
    private readonly string _connectionString;
    private readonly int _dimension;

    public MigrationRunner(string connectionString, int dimension)
    {
        _connectionString = connectionString;
        _dimension = dimension;
    }

    public static IReadOnlyList<Migration> LoadEmbedded(int dimension)
    {
        var asm = typeof(MigrationRunner).Assembly;
        var names = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n)
            .ToList();
        var result = new List<Migration>();
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing resource: {name}");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd().Replace("{{dimension}}", dimension.ToString());
            // Resource names look like CodeKb.Storage.Postgres.Migrations.001_init.sql
            // Extract just the final segment without the .sql extension.
            var stripped = name.EndsWith(".sql") ? name.Substring(0, name.Length - 4) : name;
            var lastDot = stripped.LastIndexOf('.');
            var simpleName = lastDot >= 0 ? stripped.Substring(lastDot + 1) : stripped;
            result.Add(new Migration(simpleName, sql));
        }
        return result;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Requires live Postgres.")]
    public async Task RunAsync(CancellationToken ct)
    {
        var migrations = LoadEmbedded(_dimension);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS _migrations (name TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var m in migrations)
        {
            await using var check = conn.CreateCommand();
            check.CommandText = "SELECT 1 FROM _migrations WHERE name = @name";
            check.Parameters.AddWithValue("name", m.Name);
            var exists = await check.ExecuteScalarAsync(ct);
            if (exists is not null) continue;

            await using var tx = await conn.BeginTransactionAsync(ct);
            await using (var apply = conn.CreateCommand())
            {
                apply.Transaction = tx;
                apply.CommandText = m.Sql;
                await apply.ExecuteNonQueryAsync(ct);
            }
            await using (var record = conn.CreateCommand())
            {
                record.Transaction = tx;
                record.CommandText = "INSERT INTO _migrations(name) VALUES (@name)";
                record.Parameters.AddWithValue("name", m.Name);
                await record.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }
}
