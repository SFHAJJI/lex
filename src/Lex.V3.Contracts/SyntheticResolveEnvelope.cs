using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceArtifactReference
{
    [JsonConstructor]
    public SyntheticSliceArtifactReference(string sha256)
    {
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
    }

    public string Sha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticSliceIndexReference
{
    [JsonConstructor]
    public SyntheticSliceIndexReference(string schema, string sha256, string buildId)
    {
        if (!string.Equals(schema, SyntheticSliceIndexStamp.SchemaIdentity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic runtime index schema is fixed.", nameof(schema));
        }

        Schema = schema;
        Sha256 = ContractValidation.RequireSha256(sha256, nameof(sha256));
        BuildId = ContractValidation.RequireSha256(buildId, nameof(buildId));
    }

    public string Schema { get; }

    public string Sha256 { get; }

    public string BuildId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticResolveContext
{
    [JsonConstructor]
    public SyntheticResolveContext(
        string requestRef,
        PreviewOperationReference operation,
        PreviewRefusalRegistryReference refusalRegistry,
        PreviewSnapshotReference snapshot,
        SyntheticSliceArtifactReference artifact,
        SyntheticSliceIndexReference index,
        ComponentIdentity runtime,
        ComponentIdentity builder)
    {
        RequestRef = RequireRequestReference(requestRef);
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        if (!string.Equals(operation.OperationId, "resolve", StringComparison.Ordinal) ||
            !string.Equals(
                operation.CatalogId,
                SyntheticSliceOperationCatalog.CatalogId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic response must bind the sole resolve operation.", nameof(operation));
        }

        RefusalRegistry = refusalRegistry ?? throw new ArgumentNullException(nameof(refusalRegistry));
        if (!string.Equals(
                refusalRegistry.RegistryId,
                PreviewRefusalRegistry.StageZero.RegistryId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The synthetic response must bind the reused refusal registry.",
                nameof(refusalRegistry));
        }

        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        Index = index ?? throw new ArgumentNullException(nameof(index));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        if (string.Equals(runtime.ComponentId, builder.ComponentId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Runtime and builder identities must be distinct.", nameof(builder));
        }
    }

    public string RequestRef { get; }

    public PreviewOperationReference Operation { get; }

    public PreviewRefusalRegistryReference RefusalRegistry { get; }

    public PreviewSnapshotReference Snapshot { get; }

    public SyntheticSliceArtifactReference Artifact { get; }

    public SyntheticSliceIndexReference Index { get; }

    public ComponentIdentity Runtime { get; }

    public ComponentIdentity Builder { get; }

    private static string RequireRequestReference(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 36 ||
            !value.StartsWith("req_", StringComparison.Ordinal) ||
            value.AsSpan(4).ContainsAnyExcept("0123456789abcdef"))
        {
            throw new ArgumentException(
                "A request reference must be req_ followed by 32 lowercase hexadecimal characters.",
                nameof(value));
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticHeldRecordCandidate
{
    [JsonConstructor]
    public SyntheticHeldRecordCandidate(
        IdentifierFamily identifierFamily,
        string coordinate,
        PublisherId publisher)
    {
        if (identifierFamily != IdentifierFamily.Eli ||
            !string.Equals(coordinate, ContractValidation.SyntheticEliCoordinate, StringComparison.Ordinal) ||
            publisher != PublisherId.LuLegilux)
        {
            throw new ArgumentException("The only possible held record is the exact synthetic LU ELI.");
        }

        IdentifierFamily = identifierFamily;
        Coordinate = coordinate;
        Publisher = publisher;
    }

    public IdentifierFamily IdentifierFamily { get; }

    public string Coordinate { get; }

    public PublisherId Publisher { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyntheticIdentifierUnknownRefusal
{
    [JsonConstructor]
    public SyntheticIdentifierUnknownRefusal(
        RefusalCode code,
        IdentifierFamily checkedIdentifierFamily,
        string requestedCoordinate,
        IReadOnlyList<PublisherId> publisherContextsChecked,
        IReadOnlyList<SyntheticHeldRecordCandidate> possibleHeldRecords,
        IReadOnlyList<PublisherSearchAction> officialSearchActions,
        IReadOnlyList<WhatWouldAnswerAction> whatWouldAnswer,
        bool assertsAbsenceOfLaw)
    {
        if (code != RefusalCode.IdentifierUnknown ||
            checkedIdentifierFamily != IdentifierFamily.HistoricalLegalId ||
            !string.Equals(
                requestedCoordinate,
                ContractValidation.SyntheticHistoricalLegalIdCoordinate,
                StringComparison.Ordinal) ||
            assertsAbsenceOfLaw)
        {
            throw new ArgumentException("The synthetic historical-id refusal identity is fixed.");
        }

        var publishers = (publisherContextsChecked ??
            throw new ArgumentNullException(nameof(publisherContextsChecked))).ToArray();
        if (!publishers.SequenceEqual(new[] { PublisherId.LuLegilux }))
        {
            throw new ArgumentException("The synthetic refusal checks exactly the LU publisher.", nameof(publisherContextsChecked));
        }

        var candidates = (possibleHeldRecords ??
            throw new ArgumentNullException(nameof(possibleHeldRecords))).ToArray();
        if (candidates.Length > 1 || candidates.Any(static candidate => candidate is null))
        {
            throw new ArgumentException("The exact index relation yields zero or one held ELI.", nameof(possibleHeldRecords));
        }

        var actions = (officialSearchActions ??
            throw new ArgumentNullException(nameof(officialSearchActions))).ToArray();
        if (actions.Length != 1 || actions[0] is null ||
            !string.Equals(
                ContractJson.Serialize(actions[0]),
                ContractJson.Serialize(PublisherSearchAction.Create(PublisherId.LuLegilux)),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The refusal carries only the generic official LU search action.", nameof(officialSearchActions));
        }

        var nextSteps = (whatWouldAnswer ?? throw new ArgumentNullException(nameof(whatWouldAnswer))).ToArray();
        if (nextSteps.Length == 0 ||
            nextSteps.Distinct().Count() != nextSteps.Length ||
            !nextSteps.SequenceEqual(nextSteps.OrderBy(static value => value)))
        {
            throw new ArgumentException(
                "What-would-answer actions must be non-empty, unique, and ordered.",
                nameof(whatWouldAnswer));
        }

        foreach (var nextStep in nextSteps)
        {
            ContractValidation.RequireDefined(nextStep, nameof(whatWouldAnswer));
        }

        Code = code;
        CheckedIdentifierFamily = checkedIdentifierFamily;
        RequestedCoordinate = requestedCoordinate;
        PublisherContextsChecked = Array.AsReadOnly(publishers);
        PossibleHeldRecords = Array.AsReadOnly(candidates);
        OfficialSearchActions = Array.AsReadOnly(actions);
        WhatWouldAnswer = Array.AsReadOnly(nextSteps);
        AssertsAbsenceOfLaw = false;
    }

    public RefusalCode Code { get; }

    public IdentifierFamily CheckedIdentifierFamily { get; }

    public string RequestedCoordinate { get; }

    public IReadOnlyList<PublisherId> PublisherContextsChecked { get; }

    public IReadOnlyList<SyntheticHeldRecordCandidate> PossibleHeldRecords { get; }

    public IReadOnlyList<PublisherSearchAction> OfficialSearchActions { get; }

    public IReadOnlyList<WhatWouldAnswerAction> WhatWouldAnswer { get; }

    public bool AssertsAbsenceOfLaw { get; }

    public static SyntheticIdentifierUnknownRefusal Create(
        IReadOnlyList<SyntheticHeldRecordCandidate> possibleHeldRecords) => new(
        RefusalCode.IdentifierUnknown,
        IdentifierFamily.HistoricalLegalId,
        ContractValidation.SyntheticHistoricalLegalIdCoordinate,
        new[] { PublisherId.LuLegilux },
        possibleHeldRecords,
        new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
        new[] { WhatWouldAnswerAction.CorrectedIdentifier },
        assertsAbsenceOfLaw: false);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "branch")]
[JsonDerivedType(typeof(SyntheticResolveSuccessEnvelope), "success")]
[JsonDerivedType(typeof(SyntheticResolveRefusalEnvelope), "refusal")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class SyntheticResolveEnvelope
{
    private protected SyntheticResolveEnvelope(
        string schema,
        bool synthetic,
        string objectType,
        string status,
        SyntheticResolveContext context)
    {
        if (!string.Equals(schema, V3SchemaIds.SyntheticResolveEnvelope, StringComparison.Ordinal) ||
            !synthetic ||
            !string.Equals(objectType, "envelope", StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic resolve envelope identity is fixed.");
        }

        Schema = schema;
        Synthetic = true;
        ObjectType = objectType;
        Status = ContractValidation.RequireIdentifier(status, nameof(status));
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public string Schema { get; }

    public bool Synthetic { get; }

    public string ObjectType { get; }

    public string Status { get; }

    public SyntheticResolveContext Context { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SyntheticResolveSuccessEnvelope : SyntheticResolveEnvelope
{
    public const string TrustNotice = "This text is synthetic and has no legal authority.";

    [JsonConstructor]
    public SyntheticResolveSuccessEnvelope(
        string schema,
        bool synthetic,
        string objectType,
        string status,
        SyntheticResolveContext context,
        IdentifierFamily matchedIdentifierFamily,
        string matchedCoordinate,
        PreviewObjectSet result)
        : base(schema, synthetic, objectType, status, context)
    {
        if (!string.Equals(status, "ok", StringComparison.Ordinal) ||
            matchedIdentifierFamily != IdentifierFamily.Eli ||
            !string.Equals(matchedCoordinate, ContractValidation.SyntheticEliCoordinate, StringComparison.Ordinal))
        {
            throw new ArgumentException("A success is the exact held synthetic ELI only.");
        }

        Result = result ?? throw new ArgumentNullException(nameof(result));
        if (result.Objects.Count != 1 ||
            result.Objects[0] is not PreviewSyntheticCoordinate coordinate ||
            !coordinate.Synthetic ||
            coordinate.BodyHoldingState != BodyHoldingState.HeldPublic ||
            coordinate.Body is null ||
            !coordinate.Body.Contains(TrustNotice, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A success must carry one public synthetic SQL-backed object with its trust notice.",
                nameof(result));
        }

        MatchedIdentifierFamily = matchedIdentifierFamily;
        MatchedCoordinate = matchedCoordinate;
    }

    public IdentifierFamily MatchedIdentifierFamily { get; }

    public string MatchedCoordinate { get; }

    public PreviewObjectSet Result { get; }

    public static SyntheticResolveSuccessEnvelope Create(
        SyntheticResolveContext context,
        IdentifierFamily matchedIdentifierFamily,
        string matchedCoordinate,
        PreviewObjectSet result) => new(
        V3SchemaIds.SyntheticResolveEnvelope,
        synthetic: true,
        "envelope",
        "ok",
        context,
        matchedIdentifierFamily,
        matchedCoordinate,
        result);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SyntheticResolveRefusalEnvelope : SyntheticResolveEnvelope
{
    [JsonConstructor]
    public SyntheticResolveRefusalEnvelope(
        string schema,
        bool synthetic,
        string objectType,
        string status,
        SyntheticResolveContext context,
        SyntheticIdentifierUnknownRefusal refusal)
        : base(schema, synthetic, objectType, status, context)
    {
        Refusal = refusal ?? throw new ArgumentNullException(nameof(refusal));
        if (!string.Equals(status, "identifier_unknown", StringComparison.Ordinal) ||
            refusal.Code != RefusalCode.IdentifierUnknown)
        {
            throw new ArgumentException("Refusal status and payload code must match.", nameof(status));
        }
    }

    public SyntheticIdentifierUnknownRefusal Refusal { get; }

    public static SyntheticResolveRefusalEnvelope Create(
        SyntheticResolveContext context,
        SyntheticIdentifierUnknownRefusal refusal) => new(
        V3SchemaIds.SyntheticResolveEnvelope,
        synthetic: true,
        "envelope",
        "identifier_unknown",
        context,
        refusal);
}
