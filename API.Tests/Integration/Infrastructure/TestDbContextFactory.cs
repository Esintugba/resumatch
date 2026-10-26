using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace API.Tests.Integration.Infrastructure;

public sealed class TestDbContextFactory
{
    private readonly string _connectionString;

    public TestDbContextFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A test database connection string is required.",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public AppDbContext CreateDbContext(
        params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .EnableDetailedErrors();

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new AppDbContext(optionsBuilder.Options);
    }
}
