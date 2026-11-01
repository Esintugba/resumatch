namespace Api.Application.Common.Exceptions;

/// <summary>
/// Stable machine-readable error codes returned in the ProblemDetails "code"
/// extension. Clients depend on these values: never rename one, and document
/// every addition in docs/api/error-codes.md.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string MalformedRequest = "malformed_request";
    public const string Unauthorized = "unauthorized";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string PreconditionFailed = "precondition_failed";
    public const string PreconditionRequired = "precondition_required";
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
    public const string FeatureDisabled = "feature_disabled";

    public const string RegistrationRejected = "registration_rejected";
    public const string SectionLimitReached = "section_limit_reached";
    public const string DuplicateSkill = "duplicate_skill";
    public const string DuplicateLanguage = "duplicate_language";
    public const string DuplicateApplication = "duplicate_application";
    public const string ReferenceConflict = "reference_conflict";
    public const string ResourceInUse = "resource_in_use";
    public const string InvalidStatusTransition = "invalid_status_transition";
    public const string ParseDraftStateInvalid = "parse_draft_state_invalid";
    public const string ParseDraftOutdated = "parse_draft_outdated";
    public const string SkillProvenanceLocked = "skill_provenance_locked";
    public const string ParseCooldown = "parse_cooldown";
}
