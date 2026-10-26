namespace Api.Entities;

public class Resume : BaseEntity
{
    public uint Version { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? ProfessionalTitle { get; set; }
    public string? Summary { get; set; }

    public bool IsMasterResume { get; set; }
    public bool IsArchived { get; set; }

    public Guid? SourceResumeId { get; set; }
    public bool? SourceResumeIsMaster { get; set; }

    public string? TemplateKey { get; set; }
    public string? TargetRole { get; set; }

    public DateTimeOffset? LastExportedAt { get; set; }

    public User User { get; set; } = null!;
    public Resume? SourceResume { get; set; }
    public ICollection<Resume> DerivedResumes { get; set; } = new List<Resume>();

    public ResumeContact? Contact { get; set; }
    public ICollection<ResumeLink> Links { get; set; } = new List<ResumeLink>();

    public ICollection<ResumeExperience> Experiences { get; set; } = new List<ResumeExperience>();
    public ICollection<ResumeEducation> Educations { get; set; } = new List<ResumeEducation>();
    public ICollection<ResumeSkill> Skills { get; set; } = new List<ResumeSkill>();
    public ICollection<ResumeProject> Projects { get; set; } = new List<ResumeProject>();
    public ICollection<ResumeCertification> Certifications { get; set; } = new List<ResumeCertification>();
    public ICollection<ResumeLanguage> Languages { get; set; } = new List<ResumeLanguage>();
    public ICollection<ResumeAnalysis> Analyses { get; set; } = new List<ResumeAnalysis>();
    public ICollection<ResumeExport> Exports { get; set; } = new List<ResumeExport>();
}
