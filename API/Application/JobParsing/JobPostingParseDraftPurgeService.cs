using System.Data;
using System.Data.Common;
using Api.Application.JobParsing.Diagnostics;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftPurgeService :
    IJobPostingParseDraftPurgeService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retentionPeriod;
    private readonly int _batchSize;
    private readonly ILogger<JobPostingParseDraftPurgeService> _logger;

    public JobPostingParseDraftPurgeService(
        AppDbContext dbContext,
        IOptions<JobPostingParseDraftOptions> options,
        TimeProvider timeProvider,
        ILogger<JobPostingParseDraftPurgeService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<JobPostingParseDraftPurgeService>.Instance;
        ArgumentNullException.ThrowIfNull(options);

        _retentionPeriod = options.Value.RetentionPeriod;
        _batchSize = options.Value.PurgeBatchSize;
        if (_retentionPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Draft retention period must be positive.");
        }

        if (_batchSize is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Draft purge batch size must be between 1 and 1000.");
        }
    }

    public async Task<int> PurgeBatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cutoff = _timeProvider.GetUtcNow().ToUniversalTime() - _retentionPeriod;

        try
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var connection = _dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = PurgeSql;
            AddParameter(command, "cutoff", cutoff);
            AddParameter(command, "batchSize", _batchSize);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var purged = Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);

            JobPostingParseDraftMetrics.PurgeBatches.Add(1);
            JobPostingParseDraftMetrics.PurgedDrafts.Add(purged);
            return purged;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            JobPostingParseDraftMetrics.PurgeFailures.Add(1);
            _logger.LogWarning(
                "Job parse draft purge encountered a concurrent provenance reference. " +
                "Operation={Operation} ExceptionType={ExceptionType}",
                "purge_batch",
                exception.GetType().Name);
            _dbContext.ChangeTracker.Clear();
            return 0;
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private const string PurgeSql =
        """
        /* resumatch_parse_draft_purge */
        WITH candidates AS
        (
            SELECT draft."Id",
                CASE
                    WHEN draft."Status" = 4 THEN draft."ConfirmedAt"
                    WHEN draft."Status" = 3 THEN draft."CompletedAt"
                    WHEN draft."Status" = 5 THEN
                        COALESCE(draft."CompletedAt", draft."UpdatedAt")
                    WHEN draft."Status" IN (6, 7) THEN draft."UpdatedAt"
                END AS terminal_at
            FROM "JobPostingParseDrafts" AS draft
            WHERE draft."Status" IN
                (3, 4, 5, 6, 7)
              AND CASE
                    WHEN draft."Status" = 4 THEN draft."ConfirmedAt"
                    WHEN draft."Status" = 3 THEN draft."CompletedAt"
                    WHEN draft."Status" = 5 THEN
                        COALESCE(draft."CompletedAt", draft."UpdatedAt")
                    WHEN draft."Status" IN (6, 7) THEN draft."UpdatedAt"
                  END <= @cutoff
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM "JobPostingSkills" AS requirement
                  WHERE requirement."SourceParseDraftId" = draft."Id"
                     OR EXISTS
                     (
                         SELECT 1
                         FROM "JobPostingParseDraftSkills" AS candidate
                         WHERE candidate."JobPostingParseDraftId" = draft."Id"
                           AND candidate."Id" = requirement."SourceParseDraftSkillId"
                     )
              )
            ORDER BY terminal_at ASC, draft."Id" ASC
            FOR UPDATE OF draft SKIP LOCKED
            LIMIT @batchSize
        ), deleted AS
        (
            DELETE FROM "JobPostingParseDrafts" AS draft
            USING candidates
            WHERE draft."Id" = candidates."Id"
            RETURNING draft."Id"
        )
        SELECT COUNT(*)::integer FROM deleted;
        """;
}
