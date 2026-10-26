using System.Data;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Observability;

public sealed class EfSchemaCompatibilityChecker : ISchemaCompatibilityChecker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _timeout;

    public EfSchemaCompatibilityChecker(
        IServiceScopeFactory scopeFactory,
        IOptions<ObservabilityOptions> options)
    {
        _scopeFactory = scopeFactory ??
            throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        _timeout = options.Value.DatabaseCheckTimeout;
    }

    public async Task<SchemaCompatibilityResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expected = dbContext.Database.GetMigrations()
                .ToHashSet(StringComparer.Ordinal);
            var applied = await ReadAppliedMigrationsAsync(
                dbContext,
                timeout.Token);

            return Classify(expected, applied);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SchemaCompatibilityResult(
                SchemaCompatibilityStatus.MigrationHistoryUnavailable,
                exception.GetType().Name);
        }
    }

    internal static SchemaCompatibilityResult Classify(
        IReadOnlySet<string> expected,
        IReadOnlySet<string> applied)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(applied);

        if (applied.Any(migration => !expected.Contains(migration)))
        {
            return new SchemaCompatibilityResult(
                SchemaCompatibilityStatus.DatabaseAhead);
        }

        if (expected.Any(migration => !applied.Contains(migration)))
        {
            return new SchemaCompatibilityResult(
                SchemaCompatibilityStatus.DatabaseBehind);
        }

        return new SchemaCompatibilityResult(
            SchemaCompatibilityStatus.Compatible);
    }

    private static async Task<HashSet<string>> ReadAppliedMigrationsAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var existenceCommand = connection.CreateCommand())
            {
                existenceCommand.CommandText =
                    "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL;";
                var exists = await existenceCommand.ExecuteScalarAsync(
                    cancellationToken);
                if (exists is not true)
                {
                    throw new InvalidOperationException(
                        "The migration history is unavailable.");
                }
            }

            var migrations = new HashSet<string>(StringComparer.Ordinal);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\";";
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                migrations.Add(reader.GetString(0));
            }

            return migrations;
        }
        finally
        {
            if (openedHere && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
