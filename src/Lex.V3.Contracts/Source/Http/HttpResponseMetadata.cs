using System.Globalization;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "cardinality")]
[JsonDerivedType(typeof(AbsentHttpHeader), "absent")]
[JsonDerivedType(typeof(SingleHttpHeader), "single")]
[JsonDerivedType(typeof(MultipleHttpHeader), "multiple")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class HttpHeaderField
{
    private protected HttpHeaderField()
    {
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AbsentHttpHeader : HttpHeaderField
{
    public AbsentHttpHeader()
    {
    }

}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SingleHttpHeader : HttpHeaderField
{
    [JsonConstructor]
    public SingleHttpHeader(string value)
    {
        Value = HttpResponseMetadata.RequireBoundedHeaderValue(value, nameof(value));
    }

    public string Value { get; }

}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MultipleHttpHeader : HttpHeaderField
{
    [JsonConstructor]
    public MultipleHttpHeader(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 2 or > HttpResponseMetadata.MaximumHeaderOccurrences)
        {
            throw new ArgumentException(
                $"Multiple response fields require between 2 and {HttpResponseMetadata.MaximumHeaderOccurrences} retained values.",
                nameof(values));
        }

        Values = Array.AsReadOnly(values
            .Select(static value => HttpResponseMetadata.RequireBoundedHeaderValue(value, nameof(values)))
            .ToArray());
    }

    public IReadOnlyList<string> Values { get; }

}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HttpResponseMetadata
{
    public const int MaximumHeaderValueLength = 4096;
    public const int MaximumHeaderOccurrences = 16;

    [JsonConstructor]
    public HttpResponseMetadata(
        HttpHeaderField contentType,
        HttpHeaderField declaredCharset,
        HttpHeaderField contentLength,
        HttpHeaderField contentEncoding,
        HttpHeaderField transferEncoding,
        HttpHeaderField contentRange,
        HttpHeaderField etag,
        HttpHeaderField lastModified,
        HttpHeaderField location,
        HttpHeaderField cacheControl,
        HttpHeaderField expires,
        HttpHeaderField date,
        HttpHeaderField age)
    {
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        DeclaredCharset = declaredCharset ?? throw new ArgumentNullException(nameof(declaredCharset));
        ContentLength = contentLength ?? throw new ArgumentNullException(nameof(contentLength));
        ContentEncoding = contentEncoding ?? throw new ArgumentNullException(nameof(contentEncoding));
        TransferEncoding = transferEncoding ?? throw new ArgumentNullException(nameof(transferEncoding));
        ContentRange = contentRange ?? throw new ArgumentNullException(nameof(contentRange));
        Etag = etag ?? throw new ArgumentNullException(nameof(etag));
        LastModified = lastModified ?? throw new ArgumentNullException(nameof(lastModified));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        CacheControl = cacheControl ?? throw new ArgumentNullException(nameof(cacheControl));
        Expires = expires ?? throw new ArgumentNullException(nameof(expires));
        Date = date ?? throw new ArgumentNullException(nameof(date));
        Age = age ?? throw new ArgumentNullException(nameof(age));
    }

    public HttpHeaderField ContentType { get; }

    public HttpHeaderField DeclaredCharset { get; }

    public HttpHeaderField ContentLength { get; }

    public HttpHeaderField ContentEncoding { get; }

    public HttpHeaderField TransferEncoding { get; }

    public HttpHeaderField ContentRange { get; }

    public HttpHeaderField Etag { get; }

    public HttpHeaderField LastModified { get; }

    public HttpHeaderField Location { get; }

    public HttpHeaderField CacheControl { get; }

    public HttpHeaderField Expires { get; }

    public HttpHeaderField Date { get; }

    public HttpHeaderField Age { get; }

    internal bool HasContentRange => ContentRange is not AbsentHttpHeader;

    internal bool HasMultipleField =>
        ContentType is MultipleHttpHeader ||
        DeclaredCharset is MultipleHttpHeader ||
        ContentLength is MultipleHttpHeader ||
        ContentEncoding is MultipleHttpHeader ||
        TransferEncoding is MultipleHttpHeader ||
        ContentRange is MultipleHttpHeader ||
        Etag is MultipleHttpHeader ||
        LastModified is MultipleHttpHeader;

    public bool BlocksDerivation() =>
        HasMultipleField ||
        ContentEncoding is not AbsentHttpHeader ||
        HasContentRange ||
        HasTransferEncoding && HasContentLength ||
        ContentLength is SingleHttpHeader && !TryGetSingleContentLength(out _);

    internal bool HasTransferEncoding => TransferEncoding is not AbsentHttpHeader;

    internal bool HasContentLength => ContentLength is not AbsentHttpHeader;

    internal bool TryGetSingleContentLength(out long length)
    {
        length = 0;
        return ContentLength is SingleHttpHeader single &&
            single.Value.Length > 0 &&
            single.Value.All(static character => character is >= '0' and <= '9') &&
            long.TryParse(single.Value, NumberStyles.None, CultureInfo.InvariantCulture, out length);
    }

    internal static string RequireBoundedHeaderValue(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
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
