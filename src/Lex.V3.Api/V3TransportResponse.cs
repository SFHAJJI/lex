using System.Text.Json.Serialization;

namespace Lex.V3.Api;

internal enum V3TransportFailureKind
{
    MalformedJson,
    DuplicateJsonMember,
    TrailingJsonContent,
    RequestTooLarge,
    RequestTooDeep,
    ParametersNotObject,
    RequestSchemaInvalid,
    UnknownOperation,
    MethodNotAllowed,
    UnknownRoute,
    InternalResponseInvalid,
    InternalFailure,
}

internal sealed class V3TransportFailureException : Exception
{
    public V3TransportFailureException(
        V3TransportFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public V3TransportFailureKind Kind { get; }
}

internal sealed record V3TransportProblem(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("code")] string Code);

internal static class V3TransportResponse
{
    public const string Schema = "lex-v3-transport-problem/1";
    public const int MaximumResponseBytes = 4 * 1024;

    public static Task WriteAsync(
        HttpResponse response,
        V3TransportFailureKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.HasStarted)
        {
            throw new InvalidOperationException("A transport problem cannot replace a started response.");
        }

        var (code, title, status) = Describe(kind);
        response.Clear();
        if (response.Body.CanSeek)
        {
            response.Body.SetLength(0);
            response.Body.Position = 0;
        }

        if (kind == V3TransportFailureKind.MethodNotAllowed)
        {
            response.Headers.Allow = HttpMethods.Post;
        }

        return BufferedHttpResponse.WriteJsonAsync(
            response,
            status,
            "application/problem+json",
            new V3TransportProblem(
                Schema,
                $"urn:lex:v3:transport:{code}",
                title,
                status,
                code),
            MaximumResponseBytes,
            cancellationToken);
    }

    private static (string Code, string Title, int Status) Describe(V3TransportFailureKind kind) => kind switch
    {
        V3TransportFailureKind.MalformedJson =>
            ("malformed_json", "Malformed JSON", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.DuplicateJsonMember =>
            ("duplicate_json_member", "Duplicate JSON member", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.TrailingJsonContent =>
            ("trailing_json_content", "Trailing JSON content", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.RequestTooLarge =>
            ("request_too_large", "Request too large", StatusCodes.Status413PayloadTooLarge),
        V3TransportFailureKind.RequestTooDeep =>
            ("request_too_deep", "Request too deep", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.ParametersNotObject =>
            ("parameters_not_object", "Parameters must be an object", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.RequestSchemaInvalid =>
            ("request_schema_invalid", "Request schema invalid", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.UnknownOperation =>
            ("unknown_operation", "Unknown operation", StatusCodes.Status400BadRequest),
        V3TransportFailureKind.MethodNotAllowed =>
            ("method_not_allowed", "Method not allowed", StatusCodes.Status405MethodNotAllowed),
        V3TransportFailureKind.UnknownRoute =>
            ("unknown_route", "Unknown route", StatusCodes.Status404NotFound),
        V3TransportFailureKind.InternalResponseInvalid =>
            ("internal_response_invalid", "Internal response invalid", StatusCodes.Status500InternalServerError),
        V3TransportFailureKind.InternalFailure =>
            ("internal_failure", "Internal failure", StatusCodes.Status500InternalServerError),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
