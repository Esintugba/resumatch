using Api.Application.JobParsing.Models;

namespace Api.Application.JobParsing;

public interface IJobDescriptionParser
{
    string ParserVersion { get; }

    Task<JobDescriptionParseResult> ParseAsync(
        JobDescriptionParseInput input,
        CancellationToken cancellationToken);
}
