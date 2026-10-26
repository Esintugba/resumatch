namespace Api.Infrastructure.Observability;

public enum SchemaCompatibilityStatus
{
    Compatible,
    DatabaseBehind,
    DatabaseAhead,
    MigrationHistoryUnavailable
}

public readonly record struct SchemaCompatibilityResult(
    SchemaCompatibilityStatus Status,
    string? ExceptionType = null)
{
    public bool IsCompatible => Status == SchemaCompatibilityStatus.Compatible;
}
