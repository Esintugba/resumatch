using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Rules;

public sealed record AtsScoreCalculationResult(
    decimal AtsScore,
    decimal StructureScore,
    decimal FormattingScore,
    decimal ContentScore,
    decimal KeywordScore,
    ImmutableArray<CategoryScoreResult> CategoryScores);
