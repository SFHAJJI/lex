using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherContext
{
    [JsonConstructor]
    public PublisherContext(
        string contextId,
        PublisherId publisher,
        TimelineSemantics timelineSemantics,
        string snapshotArtifactSha256)
    {
        ContextId = ContractValidation.RequireIdentifier(contextId, nameof(contextId));
        SnapshotArtifactSha256 = ContractValidation.RequireSha256(
            snapshotArtifactSha256,
            nameof(snapshotArtifactSha256));

        var expected = publisher switch
        {
            PublisherId.LuLegilux => TimelineSemantics.PublisherApplicability,
            PublisherId.EuEurLex => TimelineSemantics.OfficialConsolidationState,
            _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
        };

        if (timelineSemantics != expected)
        {
            throw new ArgumentException(
                "A publisher context must retain that publisher's exact timeline semantics.",
                nameof(timelineSemantics));
        }

        Publisher = publisher;
        TimelineSemantics = timelineSemantics;
    }

    public string ContextId { get; }

    public PublisherId Publisher { get; }

    public TimelineSemantics TimelineSemantics { get; }

    public string SnapshotArtifactSha256 { get; }

    public static PublisherContext Create(
        string contextId,
        PublisherId publisher,
        TimelineSemantics timelineSemantics,
        string snapshotArtifactSha256) =>
        new(contextId, publisher, timelineSemantics, snapshotArtifactSha256);
}

public static class PublisherContextSet
{
    public static ReadOnlyCollection<PublisherContext> Create(
        IReadOnlyCollection<PublisherContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        if (contexts.Count is < 1 or > 2)
        {
            throw new ArgumentException(
                "A publisher context set must contain one or two publishers.",
                nameof(contexts));
        }

        var copy = contexts.ToArray();
        if (copy.Select(static context => context.Publisher).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Publisher contexts must be unique by publisher.", nameof(contexts));
        }

        if (copy.Select(static context => context.ContextId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("Publisher context identifiers must be unique.", nameof(contexts));
        }

        if (copy.Length == 2 &&
            (copy[0].Publisher != PublisherId.LuLegilux || copy[1].Publisher != PublisherId.EuEurLex))
        {
            throw new ArgumentException(
                "Cross-publisher contexts must use canonical LU then EU ordering.",
                nameof(contexts));
        }

        return Array.AsReadOnly(copy);
    }
}

internal static class ContractValidation
{
    public static string RequireIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(static character => character is < ' ' or > '~'))
        {
            throw new ArgumentException("Contract identifiers must be bounded printable ASCII.", parameterName);
        }

        return value;
    }

    public static string RequireSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("SHA-256 values must be 64 lowercase hexadecimal characters.", parameterName);
        }

        return value;
    }
}
