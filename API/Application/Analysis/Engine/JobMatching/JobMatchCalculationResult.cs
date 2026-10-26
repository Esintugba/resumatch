using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.JobMatching;

public sealed record JobMatchCalculationResult(
    decimal? JobMatchScore,
    ImmutableArray<KeywordMatchResult> KeywordMatches,
    ImmutableArray<AnalysisIssueResult> Issues);
