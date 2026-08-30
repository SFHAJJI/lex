using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
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
        if (copy.Any(static context => context is null))
        {
            throw new ArgumentException("Publisher contexts cannot contain null.", nameof(contexts));
        }

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
    public const int MaximumIdentifierLength = 256;
    public const int MaximumDisplayTitleScalars = 512;
    public const string IdentifierPattern = "^(?=.*[^ ])[ -~]{1,256}$";
    public const string SyntheticRequestReference = "req_0123456789abcdef0123456789abcdef";
    public const string SyntheticEliCoordinate = "eli/synthetic-preview";
    public const string SyntheticCelexCoordinate = "celex:synthetic-preview";
    public const string SyntheticMemorialCoordinate = "memorial:synthetic-preview";
    public const string SyntheticHistoricalLegalIdCoordinate =
        "historical_legal_id:synthetic-preview";
    public const string SyntheticLuHeldRecordIdentifier = "preview:held:lu-legilux";
    public const string SyntheticEuHeldRecordIdentifier = "preview:held:eu-eurlex";

    public static string RequireIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumIdentifierLength ||
            value.Any(static character => character is < ' ' or > '~'))
        {
            throw new ArgumentException("Contract identifiers must be bounded printable ASCII.", parameterName);
        }

        return value;
    }

    public static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    public static string RequireOpaqueRequestReference(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!string.Equals(value, SyntheticRequestReference, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Stage 0 accepts only its exact synthetic request reference.",
                parameterName);
        }

        return value;
    }

    public static string RequireRequestedCoordinate(
        IdentifierFamily family,
        string value,
        string parameterName)
    {
        RequireDefined(family, nameof(family));
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var expected = family switch
        {
            IdentifierFamily.Eli => SyntheticEliCoordinate,
            IdentifierFamily.Celex => SyntheticCelexCoordinate,
            IdentifierFamily.Memorial => SyntheticMemorialCoordinate,
            IdentifierFamily.HistoricalLegalId => SyntheticHistoricalLegalIdCoordinate,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Stage 0 accepts only its exact family-specific synthetic coordinate.",
                parameterName);
        }

        return value;
    }

    public static string RequireDisplayTitle(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var scalarCount = 0;
        var containsVisibleScalar = false;
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Candidate titles must contain only valid Unicode scalar values.",
                    parameterName);
            }

            index += consumed;
            scalarCount++;
            if (scalarCount > MaximumDisplayTitleScalars ||
                Rune.GetUnicodeCategory(rune) is
                    UnicodeCategory.Control or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator)
            {
                throw new ArgumentException(
                    "Candidate titles must be bounded display text without control characters.",
                    parameterName);
            }

            containsVisibleScalar |= !IsPreviewWhitespace(rune);
        }

        if (!containsVisibleScalar)
        {
            throw new ArgumentException(
                "Candidate titles must contain visible text.",
                parameterName);
        }

        return value;
    }

    public static string RequireNonBlankUnicode(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var containsVisibleScalar = false;
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Text must contain only valid Unicode scalar values.",
                    parameterName);
            }

            index += consumed;
            containsVisibleScalar |= !IsPreviewWhitespace(rune);
        }

        if (!containsVisibleScalar)
        {
            throw new ArgumentException("Text must contain visible content.", parameterName);
        }

        return value;
    }

    internal static bool IsPreviewWhitespace(Rune rune) => rune.Value switch
    {
        >= 0x0009 and <= 0x000d => true,
        0x0020 or 0x0085 or 0x00a0 or 0x1680 => true,
        >= 0x2000 and <= 0x200a => true,
        0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 => true,
        _ => false,
    };

    public static string RequireHeldRecordIdentifier(
        PublisherId publisher,
        string value,
        string parameterName)
    {
        RequireDefined(publisher, nameof(publisher));
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var expected = publisher switch
        {
            PublisherId.LuLegilux => SyntheticLuHeldRecordIdentifier,
            PublisherId.EuEurLex => SyntheticEuHeldRecordIdentifier,
            _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
        };
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Stage 0 held-record identifiers are exact publisher-bound synthetic values.",
                parameterName);
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
