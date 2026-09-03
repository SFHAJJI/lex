using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public sealed record HttpLogicalRequestHeader
{
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal)
    {
        "authorization",
        "cookie",
        "expect",
        "forwarded",
        "proxy-authorization",
        "set-cookie",
        "x-forwarded-for",
        "x-forwarded-host",
        "x-forwarded-proto",
        "x-real-ip",
    };

    public HttpLogicalRequestHeader(string name, string value)
    {
        if (string.IsNullOrEmpty(name) ||
            name.Any(static character => !IsFieldNameCharacter(character)) ||
            !string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal) ||
            ForbiddenNames.Contains(name))
        {
            throw new ArgumentException(
                "A logical-request header name must be one admitted lowercase field name without a side channel.",
                nameof(name));
        }

        Name = name;
        Value = RoutedHttpValidation.RequireHeaderValue(value, nameof(value));
    }

    public string Name { get; }

    public string Value { get; }

    private static bool IsFieldNameCharacter(char value) =>
        value is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '!' or '#' or '$' or '%' or '&' or
            '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}

public sealed record HttpLogicalRequestBody
{
    public HttpLogicalRequestBody(ulong length, string sha256)
    {
        Length = length;
        Sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
    }

    public ulong Length { get; }

    public string Sha256 { get; }
}

public abstract class RoutedHttpHeaderField
{
    private protected RoutedHttpHeaderField()
    {
    }
}

public sealed class RoutedHttpAbsentHeader : RoutedHttpHeaderField
{
}

public sealed class RoutedHttpSingleHeader : RoutedHttpHeaderField
{
    public RoutedHttpSingleHeader(string value)
    {
        Value = RoutedHttpValidation.RequireHeaderValue(value, nameof(value));
    }

    public string Value { get; }
}

public sealed class RoutedHttpMultipleHeader : RoutedHttpHeaderField
{
    private readonly string[] _values;

    public RoutedHttpMultipleHeader(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToArray();
        if (_values.Length is < 2 or > 16 || _values.Any(static value => value is null))
        {
            throw new ArgumentException(
                "A multiple HTTP field must retain two to sixteen stored values.",
                nameof(values));
        }

        foreach (var value in _values)
        {
            RoutedHttpValidation.RequireHeaderValue(value, nameof(values));
        }
    }

    public IReadOnlyList<string> Values => Array.AsReadOnly(_values);
}

public sealed class RoutedHttpResponseHeaders
{
    public RoutedHttpResponseHeaders(
        RoutedHttpHeaderField contentType,
        RoutedHttpHeaderField contentLength,
        RoutedHttpHeaderField contentEncoding,
        RoutedHttpHeaderField transferEncoding,
        RoutedHttpHeaderField contentRange,
        RoutedHttpHeaderField etag,
        RoutedHttpHeaderField lastModified,
        RoutedHttpHeaderField location,
        RoutedHttpHeaderField cacheControl,
        RoutedHttpHeaderField expires,
        RoutedHttpHeaderField date,
        RoutedHttpHeaderField age,
        RoutedHttpHeaderField tcn)
    {
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
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
        Tcn = tcn ?? throw new ArgumentNullException(nameof(tcn));
    }

    public RoutedHttpHeaderField ContentType { get; }

    public RoutedHttpHeaderField ContentLength { get; }

    public RoutedHttpHeaderField ContentEncoding { get; }

    public RoutedHttpHeaderField TransferEncoding { get; }

    public RoutedHttpHeaderField ContentRange { get; }

    public RoutedHttpHeaderField Etag { get; }

    public RoutedHttpHeaderField LastModified { get; }

    public RoutedHttpHeaderField Location { get; }

    public RoutedHttpHeaderField CacheControl { get; }

    public RoutedHttpHeaderField Expires { get; }

    public RoutedHttpHeaderField Date { get; }

    public RoutedHttpHeaderField Age { get; }

    public RoutedHttpHeaderField Tcn { get; }
}

/// <summary>
/// Canonical request evidence. Holding or parsing this value grants no authority to send it.
/// </summary>
public sealed class HttpLogicalRequest
{
    public const string SchemaId = "lex-http-logical-request/1";
    public const string RequestedHttpVersion = "http/1.1";
    public const string VersionPolicy = "request_version_exact";
    private const int MaximumHeaders = 16;
    private static readonly string EmptySha256 =
        Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();

    private readonly HttpLogicalRequestHeader[] _headers;
    private readonly byte[] _canonicalBytes;

    private HttpLogicalRequest(
        string uri,
        HttpRequestMethod method,
        HttpLogicalRequestHeader[] headers,
        HttpLogicalRequestBody body,
        string requestPolicySha256,
        string redirectPolicySha256)
    {
        Uri = uri;
        Method = method;
        _headers = headers;
        Body = body;
        RequestPolicySha256 = requestPolicySha256;
        RedirectPolicySha256 = redirectPolicySha256;
        _canonicalBytes = RoutedHttpCanonicalJson.WriteLogicalRequest(this);
    }

    public string Schema => SchemaId;

    public string Uri { get; }

    public HttpRequestMethod Method { get; }

    public IReadOnlyList<HttpLogicalRequestHeader> Headers => Array.AsReadOnly(_headers);

    public HttpLogicalRequestBody Body { get; }

    public string RequestedVersion => RequestedHttpVersion;

    public string VersionPolicyName => VersionPolicy;

    public string RequestPolicySha256 { get; }

    public string RedirectPolicySha256 { get; }

    public static HttpLogicalRequest Create(
        string uri,
        HttpRequestMethod method,
        IReadOnlyList<HttpLogicalRequestHeader> headers,
        HttpLogicalRequestBody body,
        string requestPolicySha256,
        string redirectPolicySha256)
    {
        uri = RoutedHttpValidation.RequireAbsoluteHttpsUri(uri, nameof(uri));
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method));
        }

        ArgumentNullException.ThrowIfNull(headers);
        var headerSnapshot = headers.ToArray();
        if (headerSnapshot.Length is < 1 or > MaximumHeaders ||
            headerSnapshot.Any(static header => header is null))
        {
            throw new ArgumentException(
                "A logical request must retain one to sixteen adapter-set headers.",
                nameof(headers));
        }

        ArgumentNullException.ThrowIfNull(body);
        requestPolicySha256 = RoutedHttpValidation.RequireSha256(
            requestPolicySha256,
            nameof(requestPolicySha256));
        redirectPolicySha256 = RoutedHttpValidation.RequireSha256(
            redirectPolicySha256,
            nameof(redirectPolicySha256));

        if (method == HttpRequestMethod.Get &&
            (body.Length != 0 || !string.Equals(body.Sha256, EmptySha256, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A GET logical request must bind the empty request body.",
                nameof(body));
        }

        var contentTypeCount = headerSnapshot.Count(static header =>
            string.Equals(header.Name, "content-type", StringComparison.Ordinal));
        if (method == HttpRequestMethod.Get && contentTypeCount != 0)
        {
            throw new ArgumentException(
                "A GET logical request cannot carry a content-type header.",
                nameof(headers));
        }

        if (method == HttpRequestMethod.Post && body.Length == 0)
        {
            throw new ArgumentException(
                "A POST logical request must bind a positive retained body.",
                nameof(body));
        }

        if (method == HttpRequestMethod.Post && contentTypeCount != 1)
        {
            throw new ArgumentException(
                "A POST logical request must carry exactly one content-type header.",
                nameof(headers));
        }

        return new HttpLogicalRequest(
            uri,
            method,
            headerSnapshot,
            body,
            requestPolicySha256,
            redirectPolicySha256);
    }

    public static HttpLogicalRequest ParseAndVerify(ReadOnlySpan<byte> canonicalBytes)
    {
        try
        {
            var json = RoutedHttpValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            RoutedHttpValidation.RequireObject(root, nameof(canonicalBytes));
            RoutedHttpValidation.RequireExactPropertyNames(
                root,
                [
                    "schema", "uri", "method", "headers", "body", "requested_http_version",
                    "version_policy", "request_policy_sha256", "redirect_policy_sha256",
                ],
                nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), SchemaId, StringComparison.Ordinal) ||
                !string.Equals(
                    root.GetProperty("requested_http_version").GetString(),
                    RequestedHttpVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.GetProperty("version_policy").GetString(),
                    VersionPolicy,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The logical request names an unsupported schema or HTTP execution policy.",
                    nameof(canonicalBytes));
            }

            var method = root.GetProperty("method").GetString() switch
            {
                "GET" => HttpRequestMethod.Get,
                "POST" => HttpRequestMethod.Post,
                _ => throw new ArgumentException(
                    "A logical request method must be GET or POST.",
                    nameof(canonicalBytes)),
            };
            var headersElement = root.GetProperty("headers");
            if (headersElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Logical-request headers must be an array.", nameof(canonicalBytes));
            }

            var headers = headersElement.EnumerateArray().Select(header =>
            {
                RoutedHttpValidation.RequireExactPropertyNames(
                    header,
                    ["name", "value"],
                    nameof(canonicalBytes));
                return new HttpLogicalRequestHeader(
                    header.GetProperty("name").GetString()!,
                    header.GetProperty("value").GetString()!);
            }).ToArray();

            var bodyElement = root.GetProperty("body");
            RoutedHttpValidation.RequireExactPropertyNames(
                bodyElement,
                ["length", "sha256"],
                nameof(canonicalBytes));
            var rebuilt = Create(
                root.GetProperty("uri").GetString()!,
                method,
                headers,
                new HttpLogicalRequestBody(
                    bodyElement.GetProperty("length").GetUInt64(),
                    bodyElement.GetProperty("sha256").GetString()!),
                root.GetProperty("request_policy_sha256").GetString()!,
                root.GetProperty("redirect_policy_sha256").GetString()!);
            if (!canonicalBytes.SequenceEqual(rebuilt._canonicalBytes))
            {
                throw new ArgumentException(
                    "The logical request is not its exact canonical typed representation.",
                    nameof(canonicalBytes));
            }

            return rebuilt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                "The logical request is not one valid closed canonical object.",
                nameof(canonicalBytes),
                exception);
        }
    }

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
}

internal static partial class RoutedHttpCanonicalJson
{
    public static byte[] WriteResponseHeaders(RoutedHttpResponseHeaders value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new RoutedHttpTextWriter();
        WriteResponseHeaders(writer, value);
        return writer.ToUtf8();
    }

    public static byte[] WriteLogicalRequest(HttpLogicalRequest value)
    {
        var writer = new RoutedHttpTextWriter();
        writer.Raw("{\"schema\":");
        writer.String(HttpLogicalRequest.SchemaId);
        writer.Raw(",\"uri\":");
        writer.String(value.Uri);
        writer.Raw(",\"method\":");
        writer.String(value.Method == HttpRequestMethod.Get ? "GET" : "POST");
        writer.Raw(",\"headers\":[");
        for (var index = 0; index < value.Headers.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            var header = value.Headers[index];
            writer.Raw("{\"name\":");
            writer.String(header.Name);
            writer.Raw(",\"value\":");
            writer.String(header.Value);
            writer.Raw("}");
        }

        writer.Raw("],\"body\":{\"length\":");
        writer.UInt64(value.Body.Length);
        writer.Raw(",\"sha256\":");
        writer.String(value.Body.Sha256);
        writer.Raw("},\"requested_http_version\":\"http/1.1\",\"version_policy\":\"request_version_exact\",\"request_policy_sha256\":");
        writer.String(value.RequestPolicySha256);
        writer.Raw(",\"redirect_policy_sha256\":");
        writer.String(value.RedirectPolicySha256);
        writer.Raw("}\n");
        return writer.ToUtf8();
    }

    private static void WriteResponseHeaders(
        RoutedHttpTextWriter writer,
        RoutedHttpResponseHeaders value)
    {
        writer.Raw("{\"content_type\":");
        WriteHeaderField(writer, value.ContentType);
        writer.Raw(",\"content_length\":");
        WriteHeaderField(writer, value.ContentLength);
        writer.Raw(",\"content_encoding\":");
        WriteHeaderField(writer, value.ContentEncoding);
        writer.Raw(",\"transfer_encoding\":");
        WriteHeaderField(writer, value.TransferEncoding);
        writer.Raw(",\"content_range\":");
        WriteHeaderField(writer, value.ContentRange);
        writer.Raw(",\"etag\":");
        WriteHeaderField(writer, value.Etag);
        writer.Raw(",\"last_modified\":");
        WriteHeaderField(writer, value.LastModified);
        writer.Raw(",\"location\":");
        WriteHeaderField(writer, value.Location);
        writer.Raw(",\"cache_control\":");
        WriteHeaderField(writer, value.CacheControl);
        writer.Raw(",\"expires\":");
        WriteHeaderField(writer, value.Expires);
        writer.Raw(",\"date\":");
        WriteHeaderField(writer, value.Date);
        writer.Raw(",\"age\":");
        WriteHeaderField(writer, value.Age);
        writer.Raw(",\"tcn\":");
        WriteHeaderField(writer, value.Tcn);
        writer.Raw("}");
    }

    private static void WriteHeaderField(RoutedHttpTextWriter writer, RoutedHttpHeaderField value)
    {
        switch (value)
        {
            case RoutedHttpAbsentHeader:
                writer.Raw("{\"kind\":\"absent\"}");
                return;
            case RoutedHttpSingleHeader single:
                writer.Raw("{\"kind\":\"single\",\"value\":");
                writer.String(single.Value);
                writer.Raw("}");
                return;
            case RoutedHttpMultipleHeader multiple:
                writer.Raw("{\"kind\":\"multiple\",\"values\":[");
                for (var index = 0; index < multiple.Values.Count; index++)
                {
                    if (index > 0)
                    {
                        writer.Raw(",");
                    }

                    writer.String(multiple.Values[index]);
                }

                writer.Raw("]}");
                return;
            default:
                throw new ArgumentException("The HTTP header field union is not closed.", nameof(value));
        }
    }
}

internal sealed class RoutedHttpTextWriter
{
    private const string LowerHex = "0123456789abcdef";
    private readonly StringBuilder _builder = new();

    public void Raw(string value) => _builder.Append(value);

    public void UInt64(ulong value) => _builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public void String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                case <= '\u001f':
                    _builder.Append("\\u00");
                    _builder.Append(LowerHex[(character >> 4) & 0xf]);
                    _builder.Append(LowerHex[character & 0xf]);
                    break;
                default:
                    if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        {
                            throw new ArgumentException("Canonical JSON cannot encode a lone surrogate.", nameof(value));
                        }

                        _builder.Append(character);
                        _builder.Append(value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw new ArgumentException("Canonical JSON cannot encode a lone surrogate.", nameof(value));
                    }
                    else
                    {
                        _builder.Append(character);
                    }

                    break;
            }
        }

        _builder.Append('"');
    }

    public byte[] ToUtf8() => RoutedHttpValidation.StrictUtf8.GetBytes(_builder.ToString());
}

internal static partial class RoutedHttpValidation
{
    internal static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string RequireSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A digest must be exactly 64 lowercase hexadecimal characters.", parameterName);
        }

        return value;
    }

    public static string RequireHeaderValue(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (StrictUtf8.GetByteCount(value) > 4096 || value.Any(static character =>
                character is '\0' or '\r' or '\n' or '\u007f' ||
                character < '\u0020' && character != '\t'))
        {
            throw new ArgumentException("An HTTP header value is not one bounded API-visible value.", parameterName);
        }

        return value;
    }

    public static string RequireAbsoluteHttpsUri(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (StrictUtf8.GetByteCount(value) > 8192 ||
            value.Any(static character => character is < '!' or > '~') ||
            !value.StartsWith("https://", StringComparison.Ordinal) ||
            HasAuthorityUserInfoMarker(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            parsed.Port == 0 ||
            !IsExactDnsName(parsed.Host) ||
            !HasExactAuthoritySpelling(value, parsed) ||
            !string.Equals(value, parsed.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A routed HTTP URI must be one exact absolute HTTPS spelling without aliases or side channels.",
                parameterName);
        }

        return value;
    }

    public static string DecodeStrictUtf8(ReadOnlySpan<byte> bytes, string parameterName)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Canonical HTTP evidence must be strict UTF-8.", parameterName, exception);
        }
    }

    public static void RequireObject(JsonElement element, string parameterName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Canonical HTTP evidence must be one JSON object.", parameterName);
        }
    }

    public static void RequireExactPropertyNames(
        JsonElement element,
        IReadOnlyList<string> names,
        string parameterName)
    {
        RequireObject(element, parameterName);
        var actual = element.EnumerateObject().Select(static property => property.Name).ToArray();
        if (!actual.SequenceEqual(names, StringComparer.Ordinal))
        {
            throw new ArgumentException("Canonical HTTP evidence has missing, extra, duplicate, or reordered fields.", parameterName);
        }
    }

    private static bool IsExactDnsName(string host)
    {
        var labels = host.Split('.', StringSplitOptions.None);
        return host.Length <= 253 &&
               string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal) &&
               !IPAddress.TryParse(host, out _) &&
               labels.All(static label =>
                   label.Length is > 0 and <= 63 &&
                   label[0] != '-' && label[^1] != '-' &&
                   label.All(static character =>
                       character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'));
    }

    private static bool HasAuthorityUserInfoMarker(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        return value.AsSpan(authorityStart, authorityEnd - authorityStart).Contains('@');
    }

    private static bool HasExactAuthoritySpelling(string value, Uri parsed)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        var authority = value[authorityStart..authorityEnd];
        var expected = parsed.IsDefaultPort
            ? parsed.Host
            : $"{parsed.Host}:{parsed.Port.ToString(CultureInfo.InvariantCulture)}";
        return string.Equals(authority, expected, StringComparison.Ordinal);
    }
}
