using Api.Application.Analysis;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Analysis;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration.Analysis;

public sealed class AnalysisMetadataPersistenceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);

    public AnalysisMetadataPersistenceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AnalyzeAsync_PersistsEngineRuleCodesWithoutModification()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedIncompleteResumeAsync(
            dbContext,
            cancellation.Token);
        var engine = new AnalysisEngineV1();
        var input = await new AnalysisInputLoader(dbContext).LoadAsync(
            resume.Id,
            null,
            user.Id,
            SeedTime,
            cancellation.Token);
        var expected = engine.Analyze(input);
        Assert.True(expected.Recommendations.Length > 1);

        var response = await CreateService(dbContext, user.Id, engine)
            .AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resume.Id },
                cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.AnalysisRecommendations
            .AsNoTracking()
            .Where(recommendation =>
                recommendation.ResumeAnalysisId == response.Id)
            .OrderBy(recommendation => recommendation.SortOrder)
            .ToListAsync(cancellation.Token);

        Assert.Equal(
            expected.Recommendations.Select(recommendation =>
                recommendation.RuleCode),
            persisted.Select(recommendation => recommendation.RuleCode));
        Assert.All(persisted, recommendation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(recommendation.RuleCode));
            Assert.InRange(recommendation.RuleCode!.Length, 1, 100);
        });
    }

    [Fact]
    public async Task AnalyzeAsync_PersistsContiguousEngineRecommendationOrder()
    {
        using var cancellation = CreateCancellation();
        await using var dbContext = Fixture.DbContextFactory.CreateDbContext();
        var (user, resume) = await SeedIncompleteResumeAsync(
            dbContext,
            cancellation.Token);
        var engine = new AnalysisEngineV1();
        var input = await new AnalysisInputLoader(dbContext).LoadAsync(
            resume.Id,
            null,
            user.Id,
            SeedTime,
            cancellation.Token);
        var expected = engine.Analyze(input);
        Assert.True(expected.Recommendations.Length > 1);

        var response = await CreateService(dbContext, user.Id, engine)
            .AnalyzeAsync(
                new AnalyzeResumeRequest { ResumeId = resume.Id },
                cancellation.Token);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.AnalysisRecommendations
            .AsNoTracking()
            .Where(recommendation =>
                recommendation.ResumeAnalysisId == response.Id)
            .OrderBy(recommendation => recommendation.SortOrder)
            .ToListAsync(cancellation.Token);
        var expectedOrder = Enumerable.Range(0, expected.Recommendations.Length)
            .Select(value => (int?)value)
            .ToArray();

        Assert.Equal(expectedOrder, persisted.Select(item => item.SortOrder));
        Assert.Equal(
            persisted.Count,
            persisted.Select(item => item.SortOrder).Distinct().Count());
        Assert.Equal(
            expected.Recommendations.Select(item => item.RuleCode),
            persisted.Select(item => item.RuleCode));
    }

    [Fact]
    public void ToResponse_MapsLegacyNullMetadataAfterPersistedEngineOrderDeterministically()
    {
        var orderedFirst = CreateRecommendation(
            10,
            "ATS.TEST.010",
            0,
            RecommendationPriority.Low,
            "Ordered first");
        var orderedSecond = CreateRecommendation(
            11,
            "ATS.TEST.011",
            1,
            RecommendationPriority.High,
            "Ordered second");
        var legacyHigh = CreateRecommendation(
            12,
            null,
            null,
            RecommendationPriority.High,
            "Legacy high");
        var legacyLow = CreateRecommendation(
            13,
            null,
            null,
            RecommendationPriority.Low,
            "Legacy low");
        var analysis = CreateAnalysis();
        analysis.Recommendations.Add(legacyLow);
        analysis.Recommendations.Add(orderedSecond);
        analysis.Recommendations.Add(legacyHigh);
        analysis.Recommendations.Add(orderedFirst);

        var firstMapping = analysis.ToResponse();
        analysis.Recommendations.Clear();
        analysis.Recommendations.Add(orderedFirst);
        analysis.Recommendations.Add(legacyHigh);
        analysis.Recommendations.Add(orderedSecond);
        analysis.Recommendations.Add(legacyLow);
        var secondMapping = analysis.ToResponse();

        Assert.Equal(
            [orderedFirst.Id, orderedSecond.Id, legacyHigh.Id, legacyLow.Id],
            firstMapping.Recommendations.Select(item => item.Id));
        Assert.Equal(
            firstMapping.Recommendations.Select(item => item.Id),
            secondMapping.Recommendations.Select(item => item.Id));
        Assert.All(firstMapping.Recommendations.Skip(2), item =>
        {
            Assert.Null(item.RuleCode);
            Assert.Null(item.SortOrder);
        });
    }

    private static ResumeAnalysisService CreateService(
        AppDbContext dbContext,
        Guid userId,
        IAnalysisEngine engine)
    {
        return new ResumeAnalysisService(
            dbContext,
            new TestCurrentUserService(userId),
            new AnalysisInputLoader(dbContext),
            new AnalyzerRegistry([engine]));
    }

    private static async Task<(User User, Resume Resume)>
        SeedIncompleteResumeAsync(
            AppDbContext dbContext,
            CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Id(1),
            FirstName = "Metadata",
            LastName = "Tester",
            Email = "metadata-persistence@example.com",
            PasswordHash = "integration-test-password-hash",
            IsActive = true,
            CreatedAt = SeedTime
        };
        var resume = new Resume
        {
            Id = Id(2),
            UserId = user.Id,
            Title = "Incomplete Metadata Resume",
            IsMasterResume = true,
            CreatedAt = SeedTime
        };

        dbContext.AddRange(user, resume);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (user, resume);
    }

    private static ResumeAnalysis CreateAnalysis()
    {
        return new ResumeAnalysis
        {
            Id = Id(100),
            UserId = Id(1),
            ResumeId = Id(2),
            Type = AnalysisType.AtsOnly,
            AtsScore = 50m,
            StructureScore = 50m,
            FormattingScore = 50m,
            ContentScore = 50m,
            KeywordScore = 50m,
            AnalyzedAt = SeedTime,
            AnalyzerVersion = AnalysisEngineV1.Version,
            CreatedAt = SeedTime
        };
    }

    private static AnalysisRecommendation CreateRecommendation(
        int id,
        string? ruleCode,
        int? sortOrder,
        RecommendationPriority priority,
        string title)
    {
        return new AnalysisRecommendation
        {
            Id = Id(id),
            ResumeAnalysisId = Id(100),
            RuleCode = ruleCode,
            SortOrder = sortOrder,
            Type = RecommendationType.Other,
            Priority = priority,
            Title = title,
            Description = $"{title} description.",
            IsAiGenerated = false,
            Status = RecommendationStatus.Pending,
            CreatedAt = SeedTime
        };
    }

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static CancellationTokenSource CreateCancellation()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(45));
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "metadata-persistence@example.com";
        public bool IsAuthenticated => true;

        public Guid GetRequiredUserId()
        {
            return UserId!.Value;
        }
    }
}
