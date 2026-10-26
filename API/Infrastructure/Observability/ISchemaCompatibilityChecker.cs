namespace Api.Infrastructure.Observability;

public interface ISchemaCompatibilityChecker
{
    Task<SchemaCompatibilityResult> CheckAsync(
        CancellationToken cancellationToken = default);
}
