using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Infrastructure;

public sealed class PostgreSqlInfrastructureTests
    : IntegrationTestBase
{
    public PostgreSqlInfrastructureTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Database_HasAllEfCoreMigrationsApplied()
    {
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();

        var appliedMigrations = await dbContext.Database
            .GetAppliedMigrationsAsync();
        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync();

        Assert.NotEmpty(appliedMigrations);
        Assert.Empty(pendingMigrations);
        Assert.Contains(
            "resumatch_integration_tests",
            Fixture.ConnectionString,
            StringComparison.Ordinal);
    }
}
