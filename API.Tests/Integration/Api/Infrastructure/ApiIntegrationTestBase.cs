using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api.Infrastructure;

public abstract class ApiIntegrationTestBase : IntegrationTestBase
{
    protected ApiIntegrationTestBase(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected ResuMatchWebApplicationFactory Application { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Application = CreateApplication();
    }

    protected virtual ResuMatchWebApplicationFactory CreateApplication() =>
        new(Fixture);

    public override async Task DisposeAsync()
    {
        Application.Dispose();
        await base.DisposeAsync();
    }
}
