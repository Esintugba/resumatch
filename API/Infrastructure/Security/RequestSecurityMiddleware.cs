using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

public sealed class RequestSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestLimitOptions _options;

    public RequestSecurityMiddleware(
        RequestDelegate next,
        IOptions<RequestLimitOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var bodyLimit = context.Request.Path.StartsWithSegments(
            "/api/auth",
            StringComparison.OrdinalIgnoreCase)
            ? _options.AuthenticationMaxRequestBodySizeBytes
            : _options.MaxRequestBodySizeBytes;

        if (context.Request.ContentLength is { } length && length > bodyLimit)
        {
            await WriteBodyTooLargeAsync(context);
            return;
        }

        if (context.Request.HasFormContentType)
        {
            ApiSecurityMetrics.RequestsRejected.Add(
                1,
                new KeyValuePair<string, object?>("reason", "form_not_supported"));
            await WriteProblemAsync(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                "Form request bodies are not supported by this API.");
            return;
        }

        // Content-Length is absent for chunked requests and is not enforced by
        // every server, so the limit is also applied to the bytes actually read.
        var originalBody = context.Request.Body;
        var limitedBody = new LengthLimitedReadStream(originalBody, bodyLimit);
        context.Request.Body = limitedBody;
        try
        {
            await _next(context);
        }
        catch (Exception) when (
            limitedBody.LimitExceeded && !context.Response.HasStarted)
        {
            context.Response.Clear();
            await WriteBodyTooLargeAsync(context);
        }
        finally
        {
            context.Request.Body = originalBody;
        }
    }

    internal static Task WriteBodyTooLargeAsync(HttpContext context)
    {
        ApiSecurityMetrics.RequestsRejected.Add(
            1,
            new KeyValuePair<string, object?>("reason", "body_too_large"));
        return WriteProblemAsync(
            context,
            StatusCodes.Status413PayloadTooLarge,
            "Payload Too Large",
            "The request body exceeds the permitted size.");
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    private sealed class LengthLimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _bytesRead;

        public LengthLimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public bool LimitExceeded { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Count(_inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) =>
            Count(_inner.Read(buffer));

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            Count(await _inner.ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            Count(await _inner.ReadAsync(buffer, cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int Count(int read)
        {
            _bytesRead += read;
            if (_bytesRead > _limit)
            {
                LimitExceeded = true;
                throw new InvalidDataException(
                    "The request body exceeds the permitted size.");
            }

            return read;
        }
    }
}
