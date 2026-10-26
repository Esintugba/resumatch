using Api.DTOs.Resume;

namespace Api.Application.ResumeContacts;

public sealed record ResumeContactUpsertResult(
    ResumeContactResponse Contact,
    bool IsCreated);
