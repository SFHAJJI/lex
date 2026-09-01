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

    internal abstract IReadOnlyList<string> RetainedValues { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AbsentHttpHeader : HttpHeaderField
{
    public AbsentHttpHeader()
    {
    }

    internal override IReadOnlyList<string> RetainedValues => [];
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

    internal override IReadOnlyList<string> RetainedValues => [Value];
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

    internal override IReadOnlyList<string> RetainedValues => Values;
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
        HttpHeaderField contentRange,
        HttpHeaderField etag,
        HttpHeaderField lastModified)
    {
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        DeclaredCharset = declaredCharset ?? throw new ArgumentNullException(nameof(declaredCharset));
        ContentLength = contentLength ?? throw new ArgumentNullException(nameof(contentLength));
        ContentEncoding = contentEncoding ?? throw new ArgumentNullException(nameof(contentEncoding));
        ContentRange = contentRange ?? throw new ArgumentNullException(nameof(contentRange));
        Etag = etag ?? throw new ArgumentNullException(nameof(etag));
        LastModified = lastModified ?? throw new ArgumentNullException(nameof(lastModified));
    }

    public HttpHeaderField ContentType { get; }

    public HttpHeaderField DeclaredCharset { get; }

    public HttpHeaderField ContentLength { get; }

    public HttpHeaderField ContentEncoding { get; }

    public HttpHeaderField ContentRange { get; }

    public HttpHeaderField Etag { get; }

    public HttpHeaderField LastModified { get; }

    internal bool HasContentRange => ContentRange is not AbsentHttpHeader;

    internal bool HasMultipleField =>
        ContentType is MultipleHttpHeader ||
        DeclaredCharset is MultipleHttpHeader ||
        ContentLength is MultipleHttpHeader ||
        ContentEncoding is MultipleHttpHeader ||
        ContentRange is MultipleHttpHeader ||
        Etag is MultipleHttpHeader ||
        LastModified is MultipleHttpHeader;

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
