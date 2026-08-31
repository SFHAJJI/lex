using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lex.V3.Api;

internal sealed record PreviewProblem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status);

internal static class BoundedJsonBuffer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static byte[] Serialize<T>(T document, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidOperationException("The response exceeds its byte ceiling.");
        }

        return bytes;
    }
}

internal static class BufferedHttpResponse
{
    public static async Task WriteJsonAsync<T>(
        HttpResponse response,
        int statusCode,
        string contentType,
        T document,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var bytes = BoundedJsonBuffer.Serialize(document, maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();

        await WritePreparedJsonAsync(
            response,
            statusCode,
            contentType,
            bytes,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task WritePreparedJsonAsync(
        HttpResponse response,
        int statusCode,
        string contentType,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A prepared JSON response cannot be empty.", nameof(bytes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength = bytes.Length;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
