using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpResponseMetadata
{
    public const int MaximumHeaderValueLength = 4096;

    [JsonConstructor]
    public HttpResponseMetadata(
        string? contentType,
        string? declaredCharset,
        long? contentLength,
        string? contentEncoding,
        string? contentRange,
        string? etag,
        string? lastModified)
    {
        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentLength));
        }

        ContentType = RequireBoundedHeaderValue(contentType, nameof(contentType));
        DeclaredCharset = RequireBoundedHeaderValue(declaredCharset, nameof(declaredCharset));
        ContentLength = contentLength;
        ContentEncoding = RequireBoundedHeaderValue(contentEncoding, nameof(contentEncoding));
        ContentRange = RequireBoundedHeaderValue(contentRange, nameof(contentRange));
        Etag = RequireBoundedHeaderValue(etag, nameof(etag));
        LastModified = RequireBoundedHeaderValue(lastModified, nameof(lastModified));
    }

    public string? ContentType { get; }

    public string? DeclaredCharset { get; }

    public long? ContentLength { get; }

    public string? ContentEncoding { get; }

    public string? ContentRange { get; }

    public string? Etag { get; }

    public string? LastModified { get; }

    internal static string? RequireBoundedHeaderValue(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > MaximumHeaderValueLength ||
            value.Any(static character =>
                character is '\r' or '\n' or '\0' ||
                character < ' ' && character != '\t' ||
                character == '\u007f'))
        {
            throw new ArgumentException(
                "A retained response field must be bounded and contain no unsafe control characters.",
                parameterName);
        }

        return value;
    }
}
