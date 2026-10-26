using System.Collections.Immutable;
using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing.Engine.Validation;

public sealed class EvidenceValidator
{
    public const int MaximumEvidenceTextLength =
        ProviderJsonSchemaValidator.MaximumEvidenceTextLength;

    public void ValidateAll(
        string originalDescription,
        ImmutableArray<SkillEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(originalDescription);

        if (evidence.IsDefaultOrEmpty)
        {
            throw Error(
                "missing_evidence",
                "A skill candidate must contain evidence.");
        }

        foreach (var item in evidence)
        {
            if (item is null)
            {
                throw Error(
                    "invalid_evidence",
                    "Skill evidence cannot be null.");
            }

            Validate(originalDescription, item);
        }
    }

    public void Validate(string originalDescription, SkillEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(originalDescription);
        ArgumentNullException.ThrowIfNull(evidence);

        if (string.IsNullOrWhiteSpace(evidence.Text) ||
            evidence.Text.Length > MaximumEvidenceTextLength)
        {
            throw Error(
                "invalid_evidence_text",
                "Evidence text is empty or exceeds its allowed length.");
        }

        var end = (long)evidence.Start + evidence.Length;
        if (evidence.Start < 0 ||
            evidence.Length <= 0 ||
            end > originalDescription.Length)
        {
            throw Error(
                "evidence_out_of_range",
                "Evidence range is outside the source description.");
        }

        var endIndex = (int)end;
        if (SplitsSurrogatePair(originalDescription, evidence.Start) ||
            SplitsSurrogatePair(originalDescription, endIndex) ||
            ContainsInvalidSurrogateSequence(
                originalDescription.AsSpan(evidence.Start, evidence.Length)))
        {
            throw Error(
                "invalid_evidence_utf16_boundary",
                "Evidence range does not align with valid UTF-16 boundaries.");
        }

        if (!originalDescription.AsSpan(evidence.Start, evidence.Length)
                .SequenceEqual(evidence.Text.AsSpan()))
        {
            throw Error(
                "evidence_text_mismatch",
                "Evidence text does not match the source description.");
        }
    }

    private static bool SplitsSurrogatePair(string value, int boundary)
    {
        return boundary > 0 &&
               boundary < value.Length &&
               char.IsHighSurrogate(value[boundary - 1]) &&
               char.IsLowSurrogate(value[boundary]);
    }

    private static bool ContainsInvalidSurrogateSequence(
        ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static ParserValidationException Error(string code, string message)
    {
        return new ParserValidationException(code, message);
    }
}
