namespace Api.Application.JobParsing.Engine.Preprocessing;

public sealed record JobDescriptionPreprocessingResult(
    string OriginalDescription,
    string NormalizedMatchingText,
    string DescriptionHash);
