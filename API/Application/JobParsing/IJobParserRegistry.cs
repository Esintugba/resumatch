namespace Api.Application.JobParsing;

public interface IJobParserRegistry
{
    IJobDescriptionParser Current { get; }

    IJobDescriptionParser GetRequired(string parserVersion);
}
