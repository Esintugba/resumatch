using Api.Application.Auth;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.Auth;

public sealed class AuthSessionServiceTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly MutableTimeProvider _clock = new(Start);

    [Fact]
    public async Task Refresh_RotatesTheTokenAndExtendsTheIdleLifetime()
    {
        var (user, session, token) = await SeedSessionAsync();
        _clock.UtcNow = Start.AddDays(3);

        var result = await RefreshAsync(token);

        Assert.Equal(RefreshStatus.Succeeded, result.Status);
        Assert.NotNull(result.RefreshToken);
        Assert.NotEqual(token, result.RefreshToken);
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var stored = await dbContext.AuthSessions.AsNoTracking().SingleAsync();
        Assert.Equal(Start.AddDays(3), stored.LastUsedAt);
        Assert.Equal(Start.AddDays(17), stored.IdleExpiresAt);
        Assert.Equal(Start.AddDays(30), stored.AbsoluteExpiresAt);
        Assert.Equal(2, await dbContext.RefreshTokens.CountAsync());
        Assert.Equal(1, await dbContext.RefreshTokens.CountAsync(item => item.ConsumedAt != null));
        Assert.Equal(session.Id, result.Session!.Id);
        Assert.Equal(RefreshStatus.Succeeded, (await RefreshAsync(result.RefreshToken!)).Status);
    }

    [Fact]
    public async Task Refresh_NeverExtendsPastTheAbsoluteLifetime()
    {
        var (_, _, token) = await SeedSessionAsync();
        _clock.UtcNow = Start.AddDays(13);
        var first = await RefreshAsync(token);
        _clock.UtcNow = Start.AddDays(25);

        Assert.Equal(RefreshStatus.Succeeded, (await RefreshAsync(first.RefreshToken!)).Status);

        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(Start.AddDays(30), (await dbContext.AuthSessions.SingleAsync()).IdleExpiresAt);
    }

    [Fact]
    public async Task ConsumedToken_InsideTheGraceWindow_IsSupersededWithoutRevokingTheSession()
    {
        var (_, _, token) = await SeedSessionAsync();
        var first = await RefreshAsync(token);
        _clock.UtcNow = Start.AddSeconds(10);

        var second = await RefreshAsync(token);

        Assert.Equal(RefreshStatus.Superseded, second.Status);
        Assert.Null(second.RefreshToken);
        Assert.Null(await RevokedReasonAsync());
        Assert.Equal(RefreshStatus.Succeeded, (await RefreshAsync(first.RefreshToken!)).Status);
    }

    [Fact]
    public async Task ConsumedToken_AfterTheGraceWindow_RevokesTheWholeSession()
    {
        var (_, _, token) = await SeedSessionAsync();
        var first = await RefreshAsync(token);
        _clock.UtcNow = Start.AddSeconds(11);

        var reuse = await RefreshAsync(token);

        Assert.Equal(RefreshStatus.Reused, reuse.Status);
        Assert.Equal(AuthSessionRevocationReason.RefreshTokenReused, await RevokedReasonAsync());
        Assert.Equal(RefreshStatus.Invalid, (await RefreshAsync(first.RefreshToken!)).Status);
    }

    [Fact]
    public async Task ConcurrentRefreshes_WithOneToken_HaveExactlyOneWinner()
    {
        var (_, _, token) = await SeedSessionAsync();

        for (var round = 0; round < 10; round++)
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => RefreshAsync(token))));

            Assert.Single(results, result => result.Status == RefreshStatus.Succeeded);
            Assert.All(
                results.Where(result => result.Status != RefreshStatus.Succeeded),
                result => Assert.Equal(RefreshStatus.Superseded, result.Status));
            Assert.Null(await RevokedReasonAsync());
            token = results.Single(result => result.Status == RefreshStatus.Succeeded).RefreshToken!;
        }

        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(11, await dbContext.RefreshTokens.CountAsync());
        Assert.Equal(1, await dbContext.RefreshTokens.CountAsync(item => item.ConsumedAt == null));
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("absolute")]
    [InlineData("revoked")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    public async Task Refresh_RejectsUnusableSessions(string state)
    {
        var (user, session, token) = await SeedSessionAsync();
        switch (state)
        {
            case "idle":
                _clock.UtcNow = Start.AddDays(14);
                break;
            case "absolute":
                await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
                {
                    await dbContext.AuthSessions.ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.AbsoluteExpiresAt, Start.AddDays(1))
                        .SetProperty(item => item.IdleExpiresAt, Start.AddDays(1)));
                }

                _clock.UtcNow = Start.AddDays(1);
                break;
            case "revoked":
                await CreateService().RevokeAsync(
                    user.Id,
                    session.Id,
                    AuthSessionRevocationReason.SignedOut,
                    CancellationToken.None);
                break;
            case "inactive":
                await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
                {
                    await dbContext.Users.ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.IsActive, false));
                }

                break;
            default:
                token = RefreshTokenCodec.Create().Token;
                break;
        }

        Assert.Equal(RefreshStatus.Invalid, (await RefreshAsync(token)).Status);
    }

    [Fact]
    public async Task Start_RevokesTheOldestSessionsAboveTheLimit()
    {
        var user = await SeedUserAsync();
        var service = CreateService(maximumSessions: 2);
        var first = await service.StartAsync(user, "First", CancellationToken.None);
        _clock.UtcNow = Start.AddMinutes(1);
        var second = await service.StartAsync(user, "Second", CancellationToken.None);
        _clock.UtcNow = Start.AddMinutes(2);

        var third = await CreateService(maximumSessions: 2).StartAsync(user, "Third", CancellationToken.None);

        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var sessions = await dbContext.AuthSessions.AsNoTracking().ToListAsync();
        Assert.Equal(
            AuthSessionRevocationReason.SessionLimitReached,
            sessions.Single(item => item.Id == first.Id).RevokedReason);
        Assert.Null(sessions.Single(item => item.Id == second.Id).RevokedAt);
        Assert.Null(sessions.Single(item => item.Id == third.Id).RevokedAt);
    }

    [Fact]
    public async Task ConcurrentSignIns_StayWithinTheLimit()
    {
        var user = await SeedUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            CreateService(maximumSessions: 3).StartAsync(user, null, CancellationToken.None))));

        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(8, await dbContext.AuthSessions.CountAsync());
        Assert.Equal(3, await dbContext.AuthSessions.CountAsync(item => item.RevokedAt == null));
    }

    [Fact]
    public async Task RevokeAll_RevokesEverySessionAndChangesTheSecurityStamp()
    {
        var (user, _, token) = await SeedSessionAsync();
        await CreateService().StartAsync(user, null, CancellationToken.None);
        Guid before;
        await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
        {
            before = (await dbContext.Users.SingleAsync()).SecurityStamp;
        }

        await CreateService().RevokeAllAsync(
            user.Id,
            AuthSessionRevocationReason.SignedOutEverywhere,
            CancellationToken.None);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.NotEqual(before, (await verification.Users.SingleAsync()).SecurityStamp);
        Assert.All(
            await verification.AuthSessions.ToListAsync(),
            item => Assert.Equal(AuthSessionRevocationReason.SignedOutEverywhere, item.RevokedReason));
        Assert.Equal(RefreshStatus.Invalid, (await RefreshAsync(token)).Status);
    }

    [Fact]
    public async Task Revoke_OnlyAffectsTheOwnersSession()
    {
        var (_, session, _) = await SeedSessionAsync();

        var revoked = await CreateService().RevokeAsync(
            Guid.NewGuid(),
            session.Id,
            AuthSessionRevocationReason.SignedOut,
            CancellationToken.None);

        Assert.False(revoked);
        Assert.Null(await RevokedReasonAsync());
    }

    [Fact]
    public async Task Purge_RemovesOnlySessionsPastTheRetention()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var expired = await service.StartAsync(user, null, CancellationToken.None);
        await service.IssueRefreshTokenAsync(expired.Id, CancellationToken.None);
        _clock.UtcNow = Start.AddDays(10);
        var current = await CreateService().StartAsync(user, null, CancellationToken.None);
        await CreateService().RevokeAsync(user.Id, current.Id, AuthSessionRevocationReason.SignedOut, CancellationToken.None);

        // Retention is 7 days. The revoked session (day 10) goes after day 17;
        // the first one went idle on day 14 and goes after day 21.
        _clock.UtcNow = Start.AddDays(17).AddSeconds(-1);
        Assert.Equal(0, await CreateService().PurgeExpiredAsync(CancellationToken.None));
        _clock.UtcNow = Start.AddDays(17).AddSeconds(1);
        Assert.Equal(1, await CreateService().PurgeExpiredAsync(CancellationToken.None));
        await using (var dbContext = Fixture.DbContextFactory.CreateDbContext())
        {
            Assert.Equal(expired.Id, (await dbContext.AuthSessions.SingleAsync()).Id);
            Assert.Equal(1, await dbContext.RefreshTokens.CountAsync());
        }

        _clock.UtcNow = Start.AddDays(21).AddSeconds(1);
        Assert.Equal(1, await CreateService().PurgeExpiredAsync(CancellationToken.None));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(0, await verification.AuthSessions.CountAsync());
        Assert.Equal(0, await verification.RefreshTokens.CountAsync());
    }

    private async Task<RefreshResult> RefreshAsync(string token)
    {
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        return await CreateService(dbContext).RefreshAsync(token, CancellationToken.None);
    }

    private async Task<AuthSessionRevocationReason?> RevokedReasonAsync()
    {
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        return (await dbContext.AuthSessions.AsNoTracking().SingleAsync()).RevokedReason;
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            FirstName = "Session",
            LastName = "Test",
            Email = $"session-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private async Task<(User User, AuthSession Session, string Token)> SeedSessionAsync()
    {
        var user = await SeedUserAsync();
        var service = CreateService();
        var session = await service.StartAsync(user, "Chrome on Windows", CancellationToken.None);
        var token = await CreateService().IssueRefreshTokenAsync(session.Id, CancellationToken.None);
        return (user, session, token);
    }

    private AuthSessionService CreateService(int maximumSessions = 10) =>
        CreateService(Fixture.DbContextFactory.CreateDbContext(), maximumSessions);

    private AuthSessionService CreateService(AppDbContext dbContext, int maximumSessions = 10) =>
        new(dbContext, _clock, Options.Create(new AuthSessionOptions
        {
            MaximumActiveSessionsPerUser = maximumSessions
        }));

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _ticks = utcNow.UtcTicks;

        public DateTimeOffset UtcNow
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
