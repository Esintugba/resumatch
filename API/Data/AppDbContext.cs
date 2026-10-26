using Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<ResumeContact> ResumeContacts => Set<ResumeContact>();
    public DbSet<ResumeLink> ResumeLinks => Set<ResumeLink>();

    public DbSet<ResumeExperience> ResumeExperiences => Set<ResumeExperience>();
    public DbSet<ExperienceBullet> ExperienceBullets => Set<ExperienceBullet>();
    public DbSet<ResumeEducation> ResumeEducations => Set<ResumeEducation>();

    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<ResumeSkill> ResumeSkills => Set<ResumeSkill>();

    public DbSet<ResumeProject> ResumeProjects => Set<ResumeProject>();
    public DbSet<ProjectSkill> ProjectSkills => Set<ProjectSkill>();

    public DbSet<ResumeCertification> ResumeCertifications => Set<ResumeCertification>();
    public DbSet<ResumeLanguage> ResumeLanguages => Set<ResumeLanguage>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<JobPostingSkill> JobPostingSkills => Set<JobPostingSkill>();
    public DbSet<JobPostingParseDraft> JobPostingParseDrafts =>
        Set<JobPostingParseDraft>();
    public DbSet<JobPostingParseDraftSkill> JobPostingParseDraftSkills =>
        Set<JobPostingParseDraftSkill>();
    public DbSet<JobPostingParseDraftSkillEvidence> JobPostingParseDraftSkillEvidences =>
        Set<JobPostingParseDraftSkillEvidence>();

    public DbSet<AiUsageBudgetPeriod> AiUsageBudgetPeriods =>
        Set<AiUsageBudgetPeriod>();
    public DbSet<AiUsageReservation> AiUsageReservations =>
        Set<AiUsageReservation>();
    public DbSet<AiUsageReservationPeriod> AiUsageReservationPeriods =>
        Set<AiUsageReservationPeriod>();
    public DbSet<AiProviderConcurrencyLease> AiProviderConcurrencyLeases =>
        Set<AiProviderConcurrencyLease>();
    public DbSet<AiProviderCircuitState> AiProviderCircuitStates =>
        Set<AiProviderCircuitState>();
    public DbSet<AiProviderCircuitProbeLease> AiProviderCircuitProbeLeases =>
        Set<AiProviderCircuitProbeLease>();

    public DbSet<ResumeAnalysis> ResumeAnalyses => Set<ResumeAnalysis>();
    public DbSet<AnalysisIssue> AnalysisIssues => Set<AnalysisIssue>();
    public DbSet<KeywordMatch> KeywordMatches => Set<KeywordMatch>();
    public DbSet<AnalysisRecommendation> AnalysisRecommendations => Set<AnalysisRecommendation>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<ResumeExport> ResumeExports => Set<ResumeExport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
