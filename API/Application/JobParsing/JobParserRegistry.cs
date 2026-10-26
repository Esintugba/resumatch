namespace Api.Application.JobParsing;

public sealed class JobParserRegistry : IJobParserRegistry
{
    private readonly IReadOnlyDictionary<string, IJobDescriptionParser> _parsers;

    public JobParserRegistry(IEnumerable<IJobDescriptionParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        var parserArray = parsers.ToArray();
        if (parserArray.Length == 0 || parserArray.Any(parser => parser is null))
        {
            throw new InvalidOperationException(
                "At least one valid job description parser must be registered.");
        }

        try
        {
            _parsers = parserArray.ToDictionary(
                parser => parser.ParserVersion,
                StringComparer.Ordinal);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Job parser versions must be unique.",
                exception);
        }

        Current = GetRequired(JobParserVersions.V1);
    }

    public IJobDescriptionParser Current { get; }

    public IJobDescriptionParser GetRequired(string parserVersion)
    {
        if (string.IsNullOrWhiteSpace(parserVersion) ||
            !_parsers.TryGetValue(parserVersion, out var parser))
        {
            throw new InvalidOperationException(
                "The requested job parser version is not registered.");
        }

        return parser;
    }
}
