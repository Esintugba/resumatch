using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace API.Tests.Integration.Api.Infrastructure;

public sealed class ControlledOpenAiHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<
        RecordedOpenAiRequest,
        CancellationToken,
        Task<HttpResponseMessage>>> _responses = new();
    private readonly ConcurrentQueue<RecordedOpenAiRequest> _requests = new();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);
    public IReadOnlyCollection<RecordedOpenAiRequest> Requests =>
        _requests.ToArray();

    public void Enqueue(Func<
        RecordedOpenAiRequest,
        CancellationToken,
        Task<HttpResponseMessage>> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _responses.Enqueue(response);
    }

    public void EnqueueJson(
        HttpStatusCode statusCode,
        object body,
        string? retryAfter = null)
    {
        Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json")
            };
            if (retryAfter is not null)
            {
                response.Headers.TryAddWithoutValidation(
                    "Retry-After",
                    retryAfter);
            }

            return Task.FromResult(response);
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var recorded = new RecordedOpenAiRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
        _requests.Enqueue(recorded);
        Interlocked.Increment(ref _callCount);

        if (!_responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException(
                "No fake OpenAI response was configured for this request.");
        }

        return await response(recorded, cancellationToken);
    }
}

public sealed record RecordedOpenAiRequest(
    HttpMethod Method,
    Uri? RequestUri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string? Body);
