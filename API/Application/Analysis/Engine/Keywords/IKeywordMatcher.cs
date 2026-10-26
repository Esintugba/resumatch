using System.Collections.Immutable;
using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Keywords;

public interface IKeywordMatcher
{
    ImmutableArray<KeywordMatchResult> Match(
        ResumeSnapshot resume,
        JobPostingSnapshot jobPosting);
}
