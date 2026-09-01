using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Scope;

public static class ScopeManifestSchemaIds
{
    public const string Manifest = "lex-v3-source-scope-manifest/1";
}

public static class ScopeManifestSchemaResourceIds
{
    public const string Manifest = "urn:uuid:8c1f7247-4391-4e28-8f45-9edb60dd13aa";
}

public enum ScopeAxis
{
    [JsonStringEnumMemberName("record")]
    Record = 1,

    [JsonStringEnumMemberName("body")]
    Body = 2,

    [JsonStringEnumMemberName("relation")]
    Relation = 3,

    [JsonStringEnumMemberName("supporting_document")]
    SupportingDocument = 4,
}

public enum ScopeSelectorState
{
    [JsonStringEnumMemberName("publisher_value_present")]
    PublisherValuePresent = 1,

    [JsonStringEnumMemberName("publisher_value_absent")]
    PublisherValueAbsent = 2,

    [JsonStringEnumMemberName("publisher_value_conflict")]
    PublisherValueConflict = 3,

    [JsonStringEnumMemberName("selector_not_applicable")]
    SelectorNotApplicable = 4,
}

public enum ScopeSelectorEvidenceKind
{
    [JsonStringEnumMemberName("observed_value_set")]
    ObservedValueSet = 1,

    [JsonStringEnumMemberName("complete_observation_absence")]
    CompleteObservationAbsence = 2,

    [JsonStringEnumMemberName("observed_conflicting_value_set")]
    ObservedConflictingValueSet = 3,
}

public enum ScopeDisposition
{
    [JsonStringEnumMemberName("accepted_selected")]
    AcceptedSelected = 1,

    [JsonStringEnumMemberName("typed_quarantine")]
    TypedQuarantine = 2,

    [JsonStringEnumMemberName("point")]
    Point = 3,

    [JsonStringEnumMemberName("never_ingest")]
    NeverIngest = 4,
}

public enum ScopeRuleEffect
{
    [JsonStringEnumMemberName("positive")]
    Positive = 1,

    [JsonStringEnumMemberName("exact_denial")]
    ExactDenial = 2,
}

public enum ScopeRuleEvaluationState
{
    [JsonStringEnumMemberName("not_matched")]
    NotMatched = 1,

    [JsonStringEnumMemberName("matched")]
    Matched = 2,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeRuleBinding
{
    [JsonConstructor]
    public ScopeRuleBinding(ScopeAxis axis, int ruleMemberOrdinal, int ordinal)
    {
        Axis = ScopeValidation.RequireDefined(axis, nameof(axis));
        RuleMemberOrdinal = ScopeValidation.RequireOrdinal(
            ruleMemberOrdinal,
            nameof(ruleMemberOrdinal));
        Ordinal = ScopeValidation.RequireOrdinal(ordinal, nameof(ordinal));
    }

    public ScopeAxis Axis { get; }

    public int RuleMemberOrdinal { get; }

    public int Ordinal { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeProfileBinding
{
    [JsonConstructor]
    public ScopeProfileBinding(
        SourceArtifactRef sourceProfileRef,
        SourceArtifactRef selectorTableRef,
        IReadOnlyList<SourceRegistryMemberRef> orderedMembers,
        IReadOnlyList<int> orderedSelectorMemberOrdinals,
        IReadOnlyList<ScopeRuleBinding> orderedRules,
        int bodyCandidateRoleMemberOrdinal)
    {
        SourceProfileRef = sourceProfileRef
            ?? throw new ArgumentNullException(nameof(sourceProfileRef));
        SelectorTableRef = selectorTableRef
            ?? throw new ArgumentNullException(nameof(selectorTableRef));
        OrderedMembers = ScopeValidation.CopySortedMembers(
            orderedMembers,
            nameof(orderedMembers));
        if (OrderedMembers.Count == 0)
        {
            throw new ArgumentException("The member table cannot be empty.", nameof(orderedMembers));
        }

        foreach (var member in OrderedMembers)
        {
            if (!ScopeValidation.ArtifactEquals(member.RegistryRef, SourceProfileRef) &&
                !ScopeValidation.ArtifactEquals(member.RegistryRef, SelectorTableRef))
            {
                throw new ArgumentException(
                    "Every scope member must belong to the bound profile or selector table.",
                    nameof(orderedMembers));
            }
        }

        OrderedSelectorMemberOrdinals = ScopeValidation.CopyUniqueOrdinals(
            orderedSelectorMemberOrdinals,
            nameof(orderedSelectorMemberOrdinals));
        if (OrderedSelectorMemberOrdinals.Count == 0)
        {
            throw new ArgumentException(
                "The selector universe cannot be empty.",
                nameof(orderedSelectorMemberOrdinals));
        }

        foreach (var ordinal in OrderedSelectorMemberOrdinals)
        {
            ScopeValidation.RequireMemberOrdinal(
                ordinal,
                OrderedMembers,
                SelectorTableRef,
                nameof(orderedSelectorMemberOrdinals));
        }

        OrderedRules = ScopeValidation.CopyNonempty(orderedRules, nameof(orderedRules));
        var axes = new HashSet<ScopeAxis>();
        for (var index = 0; index < OrderedRules.Count; index++)
        {
            var rule = OrderedRules[index];
            if (rule.Ordinal != index)
            {
                throw new ArgumentException(
                    "Rule ordinals must be contiguous and equal their array position.",
                    nameof(orderedRules));
            }

            ScopeValidation.RequireMemberOrdinal(
                rule.RuleMemberOrdinal,
                OrderedMembers,
                SelectorTableRef,
                nameof(orderedRules));
            axes.Add(rule.Axis);
        }

        if (axes.Count != ScopeValidation.AllAxes.Length)
        {
            throw new ArgumentException(
                "The rule table must contain every scope axis.",
                nameof(orderedRules));
        }

        BodyCandidateRoleMemberOrdinal = ScopeValidation.RequireMemberOrdinal(
            bodyCandidateRoleMemberOrdinal,
            OrderedMembers,
            SourceProfileRef,
            nameof(bodyCandidateRoleMemberOrdinal));
    }

    public SourceArtifactRef SourceProfileRef { get; }

    public SourceArtifactRef SelectorTableRef { get; }

    public IReadOnlyList<SourceRegistryMemberRef> OrderedMembers { get; }

    public IReadOnlyList<int> OrderedSelectorMemberOrdinals { get; }

    public IReadOnlyList<ScopeRuleBinding> OrderedRules { get; }

    public int BodyCandidateRoleMemberOrdinal { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeSelectorEvidence
{
    [JsonConstructor]
    public ScopeSelectorEvidence(
        ScopeSelectorState state,
        IReadOnlyList<string> canonicalValues,
        ScopeSelectorEvidenceKind? evidenceKind,
        int? evidenceArtifactOrdinal,
        int? ruleOrdinal,
        int? causeMemberOrdinal)
    {
        State = ScopeValidation.RequireDefined(state, nameof(state));
        CanonicalValues = ScopeValidation.CopyCanonicalValues(
            canonicalValues,
            nameof(canonicalValues));
        EvidenceKind = evidenceKind is null
            ? null
            : ScopeValidation.RequireDefined(evidenceKind.Value, nameof(evidenceKind));
        EvidenceArtifactOrdinal = ScopeValidation.RequireNullableOrdinal(
            evidenceArtifactOrdinal,
            nameof(evidenceArtifactOrdinal));
        RuleOrdinal = ScopeValidation.RequireNullableOrdinal(ruleOrdinal, nameof(ruleOrdinal));
        CauseMemberOrdinal = ScopeValidation.RequireNullableOrdinal(
            causeMemberOrdinal,
            nameof(causeMemberOrdinal));

        var valid = State switch
        {
            ScopeSelectorState.PublisherValuePresent =>
                CanonicalValues.Count > 0 &&
                EvidenceKind == ScopeSelectorEvidenceKind.ObservedValueSet &&
                EvidenceArtifactOrdinal is not null &&
                RuleOrdinal is null &&
                CauseMemberOrdinal is null,
            ScopeSelectorState.PublisherValueAbsent =>
                CanonicalValues.Count == 0 &&
                EvidenceKind == ScopeSelectorEvidenceKind.CompleteObservationAbsence &&
                EvidenceArtifactOrdinal is not null &&
                RuleOrdinal is null &&
                CauseMemberOrdinal is null,
            ScopeSelectorState.PublisherValueConflict =>
                CanonicalValues.Count >= 2 &&
                EvidenceKind == ScopeSelectorEvidenceKind.ObservedConflictingValueSet &&
                EvidenceArtifactOrdinal is not null &&
                RuleOrdinal is null &&
                CauseMemberOrdinal is not null,
            ScopeSelectorState.SelectorNotApplicable =>
                CanonicalValues.Count == 0 &&
                EvidenceKind is null &&
                EvidenceArtifactOrdinal is null &&
                RuleOrdinal is not null &&
                CauseMemberOrdinal is null,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Selector state, values, and typed evidence do not form an exact variant.",
                nameof(state));
        }
    }

    public ScopeSelectorState State { get; }

    public IReadOnlyList<string> CanonicalValues { get; }

    public ScopeSelectorEvidenceKind? EvidenceKind { get; }

    public int? EvidenceArtifactOrdinal { get; }

    public int? RuleOrdinal { get; }

    public int? CauseMemberOrdinal { get; }
}

public sealed record ScopeSelectorObservationBinding(
    ScopeSelectorEvidenceKind EvidenceKind,
    string ObjectRefSha256,
    int SelectorOrdinal,
    SourceRegistryMemberRef SelectorMember,
    SourceArtifactRef SourceProfileRef,
    SourceArtifactRef SelectorTableRef,
    SourceArtifactRef EvidenceArtifactRef,
    string SelectorEvidenceSha256);

public sealed record ScopeSelectorNotApplicableBinding(
    string ObjectRefSha256,
    int SelectorOrdinal,
    SourceRegistryMemberRef SelectorMember,
    SourceArtifactRef SourceProfileRef,
    SourceArtifactRef SelectorTableRef,
    int RuleOrdinal,
    SourceRegistryMemberRef RuleMember);

public sealed record ScopeRuleEvaluationBinding(
    string ObjectRefSha256,
    string SelectorSetSha256,
    int RuleOrdinal,
    SourceRegistryMemberRef RuleMember,
    SourceArtifactRef SourceProfileRef,
    SourceArtifactRef SelectorTableRef,
    string RuleEvaluationSha256);

public sealed record ScopeCompleteEnumerationBinding(
    SourceArtifactRef CompleteEnumerationRef,
    SourceArtifactRef SourceProfileRef,
    SourceArtifactRef SelectorTableRef,
    int ObservedObjectCount,
    string ObservedObjectSequenceSha256);

public interface IScopeReductionEvidenceResolver
{
    SourceArtifactRef CompleteEnumerationRef { get; }

    bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding);

    bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding);

    bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding);

    bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeRuleEvaluation
{
    [JsonConstructor]
    public ScopeRuleEvaluation(
        int ruleOrdinal,
        ScopeRuleEvaluationState state,
        ScopeRuleEffect? effect,
        ScopeDisposition? disposition,
        IReadOnlyList<int> roleMemberOrdinals,
        IReadOnlyList<int> capabilityMemberOrdinals)
    {
        RuleOrdinal = ScopeValidation.RequireOrdinal(ruleOrdinal, nameof(ruleOrdinal));
        State = ScopeValidation.RequireDefined(state, nameof(state));
        RoleMemberOrdinals = ScopeValidation.CopySortedOrdinals(
            roleMemberOrdinals,
            nameof(roleMemberOrdinals));
        CapabilityMemberOrdinals = ScopeValidation.CopySortedOrdinals(
            capabilityMemberOrdinals,
            nameof(capabilityMemberOrdinals));

        if (State == ScopeRuleEvaluationState.NotMatched)
        {
            if (effect is not null || disposition is not null ||
                RoleMemberOrdinals.Count != 0 || CapabilityMemberOrdinals.Count != 0)
            {
                throw new ArgumentException(
                    "A not-matched evaluation cannot carry an outcome.",
                    nameof(state));
            }

            Effect = null;
            Disposition = null;
            return;
        }

        if (effect is null || disposition is null)
        {
            throw new ArgumentException(
                "A matched evaluation requires an effect and disposition.",
                nameof(state));
        }

        Effect = ScopeValidation.RequireDefined(effect.Value, nameof(effect));
        Disposition = ScopeValidation.RequireDefined(disposition.Value, nameof(disposition));
        ScopeValidation.RequireOutcomeMembers(
            Effect.Value,
            Disposition.Value,
            RoleMemberOrdinals,
            CapabilityMemberOrdinals,
            nameof(disposition));
    }

    public int RuleOrdinal { get; }

    public ScopeRuleEvaluationState State { get; }

    public ScopeRuleEffect? Effect { get; }

    public ScopeDisposition? Disposition { get; }

    public IReadOnlyList<int> RoleMemberOrdinals { get; }

    public IReadOnlyList<int> CapabilityMemberOrdinals { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeObjectReductionInput
{
    [JsonConstructor]
    public ScopeObjectReductionInput(
        SourceObjectRef objectRef,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        IReadOnlyList<ScopeRuleEvaluation> ruleEvaluations)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        Selectors = ScopeValidation.Copy(selectors, nameof(selectors));
        RuleEvaluations = ScopeValidation.Copy(ruleEvaluations, nameof(ruleEvaluations));
    }

    public SourceObjectRef ObjectRef { get; }

    public IReadOnlyList<ScopeSelectorEvidence> Selectors { get; }

    public IReadOnlyList<ScopeRuleEvaluation> RuleEvaluations { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeMatchedEvaluation
{
    [JsonConstructor]
    public ScopeMatchedEvaluation(
        int ruleOrdinal,
        ScopeRuleEffect effect,
        ScopeDisposition disposition,
        IReadOnlyList<int> roleMemberOrdinals,
        IReadOnlyList<int> capabilityMemberOrdinals)
    {
        RuleOrdinal = ScopeValidation.RequireOrdinal(ruleOrdinal, nameof(ruleOrdinal));
        Effect = ScopeValidation.RequireDefined(effect, nameof(effect));
        Disposition = ScopeValidation.RequireDefined(disposition, nameof(disposition));
        RoleMemberOrdinals = ScopeValidation.CopySortedOrdinals(
            roleMemberOrdinals,
            nameof(roleMemberOrdinals));
        CapabilityMemberOrdinals = ScopeValidation.CopySortedOrdinals(
            capabilityMemberOrdinals,
            nameof(capabilityMemberOrdinals));
        ScopeValidation.RequireOutcomeMembers(
            Effect,
            Disposition,
            RoleMemberOrdinals,
            CapabilityMemberOrdinals,
            nameof(disposition));
    }

    public int RuleOrdinal { get; }

    public ScopeRuleEffect Effect { get; }

    public ScopeDisposition Disposition { get; }

    public IReadOnlyList<int> RoleMemberOrdinals { get; }

    public IReadOnlyList<int> CapabilityMemberOrdinals { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeObservedObjectEntry
{
    [JsonConstructor]
    public ScopeObservedObjectEntry(SourceObjectRef objectRef, string objectRefSha256)
    {
        ObjectRef = objectRef ?? throw new ArgumentNullException(nameof(objectRef));
        ObjectRefSha256 = SourceCoreValidation.RequireSha256(
            objectRefSha256,
            nameof(objectRefSha256));
    }

    public SourceObjectRef ObjectRef { get; }

    public string ObjectRefSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeManifestRow
{
    [JsonConstructor]
    public ScopeManifestRow(
        int objectOrdinal,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        string ruleMatchBitsBase64Url,
        IReadOnlyList<ScopeMatchedEvaluation> matchedEvaluations,
        IReadOnlyList<int> axisWinningRuleOrdinals,
        string rowSha256)
    {
        ObjectOrdinal = ScopeValidation.RequireOrdinal(objectOrdinal, nameof(objectOrdinal));
        Selectors = ScopeValidation.Copy(selectors, nameof(selectors));
        RuleMatchBitsBase64Url = ScopeValidation.RequireBase64Url(
            ruleMatchBitsBase64Url,
            nameof(ruleMatchBitsBase64Url));
        MatchedEvaluations = ScopeValidation.Copy(matchedEvaluations, nameof(matchedEvaluations));
        AxisWinningRuleOrdinals = ScopeValidation.CopyOrdinals(
            axisWinningRuleOrdinals,
            nameof(axisWinningRuleOrdinals));
        RowSha256 = SourceCoreValidation.RequireSha256(rowSha256, nameof(rowSha256));
    }

    public int ObjectOrdinal { get; }

    public IReadOnlyList<ScopeSelectorEvidence> Selectors { get; }

    public string RuleMatchBitsBase64Url { get; }

    public IReadOnlyList<ScopeMatchedEvaluation> MatchedEvaluations { get; }

    public IReadOnlyList<int> AxisWinningRuleOrdinals { get; }

    public string RowSha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeAccountingSet
{
    [JsonConstructor]
    public ScopeAccountingSet(
        ScopeAxis axis,
        ScopeDisposition disposition,
        IReadOnlyList<int> objectOrdinals)
    {
        Axis = ScopeValidation.RequireDefined(axis, nameof(axis));
        Disposition = ScopeValidation.RequireDefined(disposition, nameof(disposition));
        ObjectOrdinals = ScopeValidation.CopySortedOrdinals(
            objectOrdinals,
            nameof(objectOrdinals));
    }

    public ScopeAxis Axis { get; }

    public ScopeDisposition Disposition { get; }

    public IReadOnlyList<int> ObjectOrdinals { get; }

    [JsonIgnore]
    public int Count => ObjectOrdinals.Count;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeManifest
{
    [JsonConstructor]
    public ScopeManifest(
        string schema,
        ScopeProfileBinding profile,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<SourceArtifactRef> orderedEvidenceArtifacts,
        IReadOnlyList<ScopeObservedObjectEntry> observedObjects,
        IReadOnlyList<ScopeManifestRow> rows,
        IReadOnlyList<ScopeAccountingSet> accounting,
        IReadOnlyList<int> bodyCandidateOrdinals)
    {
        if (!string.Equals(schema, ScopeManifestSchemaIds.Manifest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A scope manifest must declare {ScopeManifestSchemaIds.Manifest}.",
                nameof(schema));
        }

        Schema = schema;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        CompleteEnumerationRef = completeEnumerationRef
            ?? throw new ArgumentNullException(nameof(completeEnumerationRef));
        OrderedEvidenceArtifacts = ScopeValidation.CopySortedArtifacts(
            orderedEvidenceArtifacts,
            nameof(orderedEvidenceArtifacts));
        ObservedObjects = ScopeValidation.Copy(observedObjects, nameof(observedObjects));
        Rows = ScopeValidation.Copy(rows, nameof(rows));
        Accounting = ScopeValidation.Copy(accounting, nameof(accounting));
        BodyCandidateOrdinals = ScopeValidation.CopySortedOrdinals(
            bodyCandidateOrdinals,
            nameof(bodyCandidateOrdinals));
    }

    public string Schema { get; }

    public ScopeProfileBinding Profile { get; }

    public SourceArtifactRef CompleteEnumerationRef { get; }

    public IReadOnlyList<SourceArtifactRef> OrderedEvidenceArtifacts { get; }

    public IReadOnlyList<ScopeObservedObjectEntry> ObservedObjects { get; }

    public IReadOnlyList<ScopeManifestRow> Rows { get; }

    public IReadOnlyList<ScopeAccountingSet> Accounting { get; }

    public IReadOnlyList<int> BodyCandidateOrdinals { get; }
}

public sealed record ScopeAxisResult(
    ScopeAxis Axis,
    int WinningRuleOrdinal,
    ScopeRuleEffect Effect,
    ScopeDisposition Disposition,
    IReadOnlyList<int> RoleMemberOrdinals,
    IReadOnlyList<int> CapabilityMemberOrdinals);

public sealed record ScopeRequestReduction(
    SourceObjectRef ObjectRef,
    IReadOnlyList<ScopeAxis> RequestedAxes,
    IReadOnlyList<ScopeAxisResult> AllAxisResults,
    ScopeDisposition CompositeDisposition,
    IReadOnlyList<int> CompositeCapabilityMemberOrdinals);

public sealed class VerifiedScopeManifest
{
    internal VerifiedScopeManifest(ScopeManifest manifest)
    {
        Manifest = manifest;
    }

    internal ScopeManifest Manifest { get; }
}

public sealed class ScopeManifestWriteReceipt
{
    internal ScopeManifestWriteReceipt(
        string manifestSha256,
        string inputSequenceSha256,
        long canonicalByteCount,
        int objectCount)
    {
        ManifestSha256 = SourceCoreValidation.RequireSha256(
            manifestSha256,
            nameof(manifestSha256));
        InputSequenceSha256 = SourceCoreValidation.RequireSha256(
            inputSequenceSha256,
            nameof(inputSequenceSha256));
        if (canonicalByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canonicalByteCount));
        }

        CanonicalByteCount = canonicalByteCount;
        ObjectCount = ScopeValidation.RequireOrdinal(objectCount, nameof(objectCount));
    }

    public string ManifestSha256 { get; }

    public string Schema => ScopeManifestSchemaIds.Manifest;

    public string InputSequenceSha256 { get; }

    public long CanonicalByteCount { get; }

    public int ObjectCount { get; }
}

public delegate IEnumerable<ScopeObjectReductionInput> OpenCanonicalScopePass(
    CancellationToken cancellationToken);

public enum ScopeManifestReaderOnlyInvariant
{
    CanonicalTablesSortedAndUnique = 1,
    EveryOrdinalResolves = 2,
    RowsExactlyCoverObservedObjects = 3,
    RuleBitVectorLengthAndPadding = 4,
    RuleBitAndMatchedPayloadParity = 5,
    TypedSelectorObservationAdmission = 6,
    AxisWinnerRecomputation = 7,
    ExpandedRowDigestRecomputation = 8,
    ExactAccountingPartitions = 9,
    ExactBodyCandidateProjection = 10,
    CanonicalRequestValidation = 11,
    EvidenceArtifactTableExactCoverage = 12,
    ExactRuleEvaluationAdmission = 13,
    CompleteEnumerationAdmission = 14,
}

public static class ScopeManifestReaderOnlyInvariants
{
    public static IReadOnlyList<ScopeManifestReaderOnlyInvariant> All { get; } =
        new ReadOnlyCollection<ScopeManifestReaderOnlyInvariant>(
            Enum.GetValues<ScopeManifestReaderOnlyInvariant>());
}

public static class ScopeManifestCanonicalWriter
{
    private const string ManifestDomain = "lex-v3-source-scope-manifest/1\n";
    private const string RowDomain = "lex-v3-source-scope-row/1\n";
    private const string ObjectRefDomain = "lex-v3-source-object-ref/1\n";
    private const string SelectorEvidenceDomain = "lex-v3-source-scope-selector-evidence/1\n";
    private const string SelectorSetDomain = "lex-v3-source-scope-selector-set/1\n";
    private const string RuleEvaluationDomain = "lex-v3-source-scope-rule-evaluation/1\n";
    private const string InputSequenceDomain = "lex-v3-source-scope-input-sequence/1\n";
    private const string ObservedObjectSequenceDomain =
        "lex-v3-source-scope-observed-object-sequence/1\n";
    private const int ProjectionWidth = 5;

    public static string Write(Stream destination, VerifiedScopeManifest verified)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(verified);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The canonical destination must be writable.", nameof(destination));
        }

        using var hashing = new HashingWriteStream(destination, ManifestDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteManifest(writer, verified.Manifest);
            writer.Flush();
        }

        hashing.WriteByte((byte)'\n');
        return hashing.GetHashAndReset();
    }

    public static ScopeManifestWriteReceipt WriteStreaming(
        Stream destination,
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> orderedEvidenceArtifacts,
        int expectedObjectCount,
        OpenCanonicalScopePass openCanonicalSnapshot,
        IScopeReductionEvidenceResolver observationResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(orderedEvidenceArtifacts);
        ArgumentNullException.ThrowIfNull(openCanonicalSnapshot);
        ArgumentNullException.ThrowIfNull(observationResolver);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The canonical destination must be writable.",
                nameof(destination));
        }

        ScopeValidation.RequireOrdinal(expectedObjectCount, nameof(expectedObjectCount));
        var evidenceArtifacts = ScopeValidation.CopySortedArtifacts(
            orderedEvidenceArtifacts,
            nameof(orderedEvidenceArtifacts));
        var projections = GC.AllocateUninitializedArray<byte>(
            checked(expectedObjectCount * ProjectionWidth));
        var usedEvidenceArtifacts = new bool[evidenceArtifacts.Count];

        string firstSequenceSha256;
        string secondSequenceSha256;
        using var hashing = new HashingWriteStream(destination, ManifestDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteManifestHeader(
                writer,
                profile,
                observationResolver.CompleteEnumerationRef,
                evidenceArtifacts);
            writer.WritePropertyName("observed_objects");
            writer.WriteStartArray();
            firstSequenceSha256 = WriteObservedObjectPass(
                writer,
                profile,
                evidenceArtifacts,
                expectedObjectCount,
                openCanonicalSnapshot,
                observationResolver,
                projections,
                usedEvidenceArtifacts,
                cancellationToken,
                out var observedObjectSequenceSha256);
            writer.WriteEndArray();
            ScopeReducer.VerifyCompleteEnumeration(
                profile,
                observationResolver.CompleteEnumerationRef,
                expectedObjectCount,
                observedObjectSequenceSha256,
                observationResolver);

            writer.WritePropertyName("rows");
            writer.WriteStartArray();
            secondSequenceSha256 = WriteRowPass(
                writer,
                profile,
                evidenceArtifacts,
                expectedObjectCount,
                openCanonicalSnapshot,
                observationResolver,
                projections,
                cancellationToken);
            writer.WriteEndArray();

            if (!string.Equals(
                    firstSequenceSha256,
                    secondSequenceSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The canonical snapshot changed between manifest passes.");
            }

            if (usedEvidenceArtifacts.Any(static used => !used))
            {
                throw new InvalidOperationException(
                    "The evidence-artifact table must contain exactly the referenced artifacts.");
            }

            WriteProjectionAccounting(
                writer,
                projections,
                expectedObjectCount,
                cancellationToken);
            WriteProjectionBodyCandidates(
                writer,
                projections,
                expectedObjectCount,
                cancellationToken);
            writer.WriteEndObject();
            writer.Flush();
        }

        hashing.WriteByte((byte)'\n');
        var byteCount = hashing.BytesWritten;
        var manifestSha256 = hashing.GetHashAndReset();
        return new ScopeManifestWriteReceipt(
            manifestSha256,
            firstSequenceSha256,
            byteCount,
            expectedObjectCount);
    }

    private static string WriteObservedObjectPass(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        int expectedObjectCount,
        OpenCanonicalScopePass openCanonicalSnapshot,
        IScopeReductionEvidenceResolver observationResolver,
        byte[] projections,
        bool[] usedEvidenceArtifacts,
        CancellationToken cancellationToken,
        out string observedObjectSequenceSha256)
    {
        using var sequenceHash = CreateInputSequenceHash();
        using var observedSequenceHash = CreateDomainHash(ObservedObjectSequenceDomain);
        using var inputs = OpenSnapshot(openCanonicalSnapshot, cancellationToken);
        string? previousObjectSha256 = null;
        for (var ordinal = 0; ordinal < expectedObjectCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputs.MoveNext())
            {
                throw new InvalidOperationException(
                    "The canonical snapshot ended before its declared object count.");
            }

            var input = inputs.Current
                ?? throw new InvalidOperationException("A canonical snapshot yielded a null input.");
            var observed = CreateCanonicalObservedObject(input.ObjectRef, previousObjectSha256);
            var reduced = ScopeReducer.ReduceCanonicalRow(
                profile,
                evidenceArtifacts,
                observed,
                ordinal,
                input,
                observationResolver);
            WriteObservedObject(writer, observed);
            WriteProjection(
                projections.AsSpan(ordinal * ProjectionWidth, ProjectionWidth),
                reduced.Results,
                profile.BodyCandidateRoleMemberOrdinal);
            MarkUsedEvidence(input.Selectors, usedEvidenceArtifacts);
            AppendInputSequence(sequenceHash, ordinal, observed.ObjectRefSha256, reduced.Row.RowSha256);
            AppendObservedObjectSequence(
                observedSequenceHash,
                ordinal,
                observed.ObjectRefSha256);
            previousObjectSha256 = observed.ObjectRefSha256;
        }

        RequireSnapshotEnd(inputs);
        observedObjectSequenceSha256 = CompleteHash(observedSequenceHash);
        return CompleteHash(sequenceHash);
    }

    private static string WriteRowPass(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        int expectedObjectCount,
        OpenCanonicalScopePass openCanonicalSnapshot,
        IScopeReductionEvidenceResolver observationResolver,
        byte[] projections,
        CancellationToken cancellationToken)
    {
        using var sequenceHash = CreateInputSequenceHash();
        using var inputs = OpenSnapshot(openCanonicalSnapshot, cancellationToken);
        string? previousObjectSha256 = null;
        Span<byte> actualProjection = stackalloc byte[ProjectionWidth];
        for (var ordinal = 0; ordinal < expectedObjectCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputs.MoveNext())
            {
                throw new InvalidOperationException(
                    "The canonical snapshot ended before its declared object count.");
            }

            var input = inputs.Current
                ?? throw new InvalidOperationException("A canonical snapshot yielded a null input.");
            var observed = CreateCanonicalObservedObject(input.ObjectRef, previousObjectSha256);
            var reduced = ScopeReducer.ReduceCanonicalRow(
                profile,
                evidenceArtifacts,
                observed,
                ordinal,
                input,
                observationResolver);
            WriteProjection(
                actualProjection,
                reduced.Results,
                profile.BodyCandidateRoleMemberOrdinal);
            if (!actualProjection.SequenceEqual(
                    projections.AsSpan(ordinal * ProjectionWidth, ProjectionWidth)))
            {
                throw new InvalidOperationException(
                    "The scope projection changed between manifest passes.");
            }

            WriteRow(writer, reduced.Row);
            AppendInputSequence(sequenceHash, ordinal, observed.ObjectRefSha256, reduced.Row.RowSha256);
            previousObjectSha256 = observed.ObjectRefSha256;
        }

        RequireSnapshotEnd(inputs);
        return CompleteHash(sequenceHash);
    }

    private static IEnumerator<ScopeObjectReductionInput> OpenSnapshot(
        OpenCanonicalScopePass openCanonicalSnapshot,
        CancellationToken cancellationToken)
    {
        var snapshot = openCanonicalSnapshot(cancellationToken)
            ?? throw new InvalidOperationException("The canonical snapshot factory returned null.");
        return snapshot.GetEnumerator();
    }

    private static void RequireSnapshotEnd(IEnumerator<ScopeObjectReductionInput> inputs)
    {
        if (inputs.MoveNext())
        {
            throw new InvalidOperationException(
                "The canonical snapshot exceeded its declared object count.");
        }
    }

    private static ScopeObservedObjectEntry CreateCanonicalObservedObject(
        SourceObjectRef objectRef,
        string? previousObjectSha256)
    {
        ArgumentNullException.ThrowIfNull(objectRef);
        var digest = ComputeObjectRefSha256(objectRef);
        if (previousObjectSha256 is not null &&
            string.CompareOrdinal(previousObjectSha256, digest) >= 0)
        {
            throw new InvalidOperationException(
                "Streaming snapshot objects must be strictly digest-sorted and collision-free.");
        }

        return new ScopeObservedObjectEntry(objectRef, digest);
    }

    private static void WriteProjection(
        Span<byte> destination,
        IReadOnlyList<ScopeAxisResult> results,
        int bodyCandidateRoleMemberOrdinal)
    {
        if (destination.Length != ProjectionWidth ||
            results.Count != ScopeValidation.AllAxes.Length)
        {
            throw new InvalidOperationException("A scope projection must cover all four axes.");
        }

        for (var index = 0; index < ScopeValidation.AllAxes.Length; index++)
        {
            var result = results[index];
            if (result.Axis != ScopeValidation.AllAxes[index])
            {
                throw new InvalidOperationException(
                    "A scope projection is not in the fixed axis order.");
            }

            destination[index] = ProjectionDispositionByte(result.Disposition);
        }

        var body = results[(int)ScopeAxis.Body - 1];
        destination[4] = body.Disposition == ScopeDisposition.AcceptedSelected &&
            body.RoleMemberOrdinals.Contains(bodyCandidateRoleMemberOrdinal)
                ? (byte)1
                : (byte)0;
    }

    private static byte ProjectionDispositionByte(ScopeDisposition disposition) => disposition switch
    {
        ScopeDisposition.AcceptedSelected => 1,
        ScopeDisposition.TypedQuarantine => 2,
        ScopeDisposition.Point => 3,
        ScopeDisposition.NeverIngest => 4,
        _ => throw new InvalidOperationException("Unknown scope disposition."),
    };

    private static void MarkUsedEvidence(
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        bool[] usedEvidenceArtifacts)
    {
        foreach (var selector in selectors)
        {
            if (selector.EvidenceArtifactOrdinal is { } ordinal)
            {
                usedEvidenceArtifacts[ordinal] = true;
            }
        }
    }

    private static IncrementalHash CreateInputSequenceHash() =>
        CreateDomainHash(InputSequenceDomain);

    private static IncrementalHash CreateDomainHash(string domainValue)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> domain = stackalloc byte[domainValue.Length];
        WriteAscii(domain, domainValue);
        hash.AppendData(domain);
        return hash;
    }

    private static void AppendObservedObjectSequence(
        IncrementalHash hash,
        int ordinal,
        string objectRefSha256)
    {
        Span<byte> preimage = stackalloc byte[4 + 32];
        BinaryPrimitives.WriteInt32BigEndian(preimage, ordinal);
        DecodeSha256(objectRefSha256, preimage[4..]);
        hash.AppendData(preimage);
    }

    private static void AppendInputSequence(
        IncrementalHash hash,
        int ordinal,
        string objectRefSha256,
        string rowSha256)
    {
        Span<byte> preimage = stackalloc byte[4 + 32 + 32];
        BinaryPrimitives.WriteInt32BigEndian(preimage, ordinal);
        DecodeSha256(objectRefSha256, preimage[4..36]);
        DecodeSha256(rowSha256, preimage[36..]);

        hash.AppendData(preimage);
    }

    private static void DecodeSha256(string value, Span<byte> destination)
    {
        if (value.Length != 64 || destination.Length != 32)
        {
            throw new InvalidOperationException("A scope input sequence contained an invalid digest.");
        }

        for (var index = 0; index < destination.Length; index++)
        {
            var high = HexNibble(value[index * 2]);
            var low = HexNibble(value[(index * 2) + 1]);
            if (high < 0 || low < 0)
            {
                throw new InvalidOperationException(
                    "A scope input sequence contained an invalid digest.");
            }

            destination[index] = (byte)((high << 4) | low);
        }
    }

    private static int HexNibble(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => -1,
    };

    private static string CompleteHash(IncrementalHash hash)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static void WriteProjectionAccounting(
        Utf8JsonWriter writer,
        byte[] projections,
        int objectCount,
        CancellationToken cancellationToken)
    {
        writer.WritePropertyName("accounting");
        writer.WriteStartArray();
        for (var axisIndex = 0; axisIndex < ScopeValidation.AllAxes.Length; axisIndex++)
        {
            var axis = ScopeValidation.AllAxes[axisIndex];
            foreach (var disposition in ScopeValidation.AllDispositions)
            {
                writer.WriteStartObject();
                writer.WriteString("axis", AxisName(axis));
                writer.WriteString("disposition", DispositionName(disposition));
                writer.WritePropertyName("object_ordinals");
                writer.WriteStartArray();
                for (var ordinal = 0; ordinal < objectCount; ordinal++)
                {
                    if ((ordinal & 4095) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (projections[(ordinal * ProjectionWidth) + axisIndex] ==
                        ProjectionDispositionByte(disposition))
                    {
                        writer.WriteNumberValue(ordinal);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteProjectionBodyCandidates(
        Utf8JsonWriter writer,
        byte[] projections,
        int objectCount,
        CancellationToken cancellationToken)
    {
        writer.WritePropertyName("body_candidate_ordinals");
        writer.WriteStartArray();
        for (var ordinal = 0; ordinal < objectCount; ordinal++)
        {
            if ((ordinal & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (projections[(ordinal * ProjectionWidth) + 4] == 1)
            {
                writer.WriteNumberValue(ordinal);
            }
        }

        writer.WriteEndArray();
    }

    public static string ComputeObjectRefSha256(SourceObjectRef value)
    {
        using var hashing = new HashingWriteStream(Stream.Null, ObjectRefDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteObjectRef(writer, value);
            writer.Flush();
        }

        return hashing.GetHashAndReset();
    }

    internal static string ComputeObservedObjectSequenceSha256(
        IReadOnlyList<ScopeObservedObjectEntry> observedObjects)
    {
        ArgumentNullException.ThrowIfNull(observedObjects);
        using var hash = CreateDomainHash(ObservedObjectSequenceDomain);
        for (var ordinal = 0; ordinal < observedObjects.Count; ordinal++)
        {
            AppendObservedObjectSequence(hash, ordinal, observedObjects[ordinal].ObjectRefSha256);
        }

        return CompleteHash(hash);
    }

    internal static string ComputeExpandedRowSha256(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        SourceObjectRef objectRef,
        string objectRefSha256,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        IReadOnlyList<ScopeRuleEvaluation> evaluations,
        IReadOnlyList<ScopeAxisResult> axisResults)
    {
        using var hashing = new HashingWriteStream(Stream.Null, RowDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteExpandedRow(
                writer,
                profile,
                evidenceArtifacts,
                objectRef,
                objectRefSha256,
                selectors,
                evaluations,
                axisResults);
            writer.Flush();
        }

        return hashing.GetHashAndReset();
    }

    internal static string ComputeSelectorEvidenceSha256(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        int selectorOrdinal,
        ScopeSelectorEvidence selector)
    {
        using var hashing = new HashingWriteStream(Stream.Null, SelectorEvidenceDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteExpandedSelector(
                writer,
                profile,
                evidenceArtifacts,
                selectorOrdinal,
                selector);
            writer.Flush();
        }

        return hashing.GetHashAndReset();
    }

    internal static string ComputeSelectorSetSha256(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        IReadOnlyList<ScopeSelectorEvidence> selectors)
    {
        using var hashing = new HashingWriteStream(Stream.Null, SelectorSetDomain);
        using (var writer = NewWriter(hashing))
        {
            writer.WriteStartArray();
            for (var selectorOrdinal = 0;
                 selectorOrdinal < selectors.Count;
                 selectorOrdinal++)
            {
                WriteExpandedSelector(
                    writer,
                    profile,
                    evidenceArtifacts,
                    selectorOrdinal,
                    selectors[selectorOrdinal]);
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return hashing.GetHashAndReset();
    }

    internal static string ComputeRuleEvaluationSha256(
        ScopeProfileBinding profile,
        ScopeRuleEvaluation evaluation)
    {
        using var hashing = new HashingWriteStream(Stream.Null, RuleEvaluationDomain);
        using (var writer = NewWriter(hashing))
        {
            WriteExpandedEvaluation(writer, profile, evaluation);
            writer.Flush();
        }

        return hashing.GetHashAndReset();
    }

    private static Utf8JsonWriter NewWriter(Stream output) => new(
        output,
        new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        });

    private static void WriteManifest(Utf8JsonWriter writer, ScopeManifest value)
    {
        WriteManifestHeader(
            writer,
            value.Profile,
            value.CompleteEnumerationRef,
            value.OrderedEvidenceArtifacts);
        WriteArray(writer, "observed_objects", value.ObservedObjects, WriteObservedObject);
        WriteArray(writer, "rows", value.Rows, WriteRow);
        WriteArray(writer, "accounting", value.Accounting, WriteAccounting);
        WriteIntArray(writer, "body_candidate_ordinals", value.BodyCandidateOrdinals);
        writer.WriteEndObject();
    }

    private static void WriteManifestHeader(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", ScopeManifestSchemaIds.Manifest);
        writer.WritePropertyName("profile");
        WriteProfile(writer, profile);
        writer.WritePropertyName("complete_enumeration_ref");
        WriteArtifact(writer, completeEnumerationRef);
        WriteArray(
            writer,
            "ordered_evidence_artifacts",
            evidenceArtifacts,
            WriteArtifact);
    }

    private static void WriteProfile(Utf8JsonWriter writer, ScopeProfileBinding value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("source_profile_ref");
        WriteArtifact(writer, value.SourceProfileRef);
        writer.WritePropertyName("selector_table_ref");
        WriteArtifact(writer, value.SelectorTableRef);
        WriteArray(writer, "ordered_members", value.OrderedMembers, WriteMember);
        WriteIntArray(
            writer,
            "ordered_selector_member_ordinals",
            value.OrderedSelectorMemberOrdinals);
        WriteArray(writer, "ordered_rules", value.OrderedRules, WriteRuleBinding);
        writer.WriteNumber(
            "body_candidate_role_member_ordinal",
            value.BodyCandidateRoleMemberOrdinal);
        writer.WriteEndObject();
    }

    private static void WriteRuleBinding(Utf8JsonWriter writer, ScopeRuleBinding value)
    {
        writer.WriteStartObject();
        writer.WriteString("axis", AxisName(value.Axis));
        writer.WriteNumber("rule_member_ordinal", value.RuleMemberOrdinal);
        writer.WriteNumber("ordinal", value.Ordinal);
        writer.WriteEndObject();
    }

    private static void WriteObservedObject(Utf8JsonWriter writer, ScopeObservedObjectEntry value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("object_ref");
        WriteObjectRef(writer, value.ObjectRef);
        writer.WriteString("object_ref_sha256", value.ObjectRefSha256);
        writer.WriteEndObject();
    }

    private static void WriteRow(Utf8JsonWriter writer, ScopeManifestRow value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("object_ordinal", value.ObjectOrdinal);
        WriteArray(writer, "selectors", value.Selectors, WriteSelector);
        writer.WriteString("rule_match_bits_base64_url", value.RuleMatchBitsBase64Url);
        WriteArray(
            writer,
            "matched_evaluations",
            value.MatchedEvaluations,
            WriteMatchedEvaluation);
        WriteIntArray(
            writer,
            "axis_winning_rule_ordinals",
            value.AxisWinningRuleOrdinals);
        writer.WriteString("row_sha256", value.RowSha256);
        writer.WriteEndObject();
    }

    private static void WriteSelector(Utf8JsonWriter writer, ScopeSelectorEvidence value)
    {
        writer.WriteStartObject();
        writer.WriteString("state", SelectorStateName(value.State));
        writer.WritePropertyName("canonical_values");
        writer.WriteStartArray();
        foreach (var item in value.CanonicalValues)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
        WriteNullableString(
            writer,
            "evidence_kind",
            value.EvidenceKind is null ? null : EvidenceKindName(value.EvidenceKind.Value));
        WriteNullableInt(writer, "evidence_artifact_ordinal", value.EvidenceArtifactOrdinal);
        WriteNullableInt(writer, "rule_ordinal", value.RuleOrdinal);
        WriteNullableInt(writer, "cause_member_ordinal", value.CauseMemberOrdinal);
        writer.WriteEndObject();
    }

    private static void WriteMatchedEvaluation(
        Utf8JsonWriter writer,
        ScopeMatchedEvaluation value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("rule_ordinal", value.RuleOrdinal);
        writer.WriteString("effect", EffectName(value.Effect));
        writer.WriteString("disposition", DispositionName(value.Disposition));
        WriteIntArray(writer, "role_member_ordinals", value.RoleMemberOrdinals);
        WriteIntArray(writer, "capability_member_ordinals", value.CapabilityMemberOrdinals);
        writer.WriteEndObject();
    }

    private static void WriteAccounting(Utf8JsonWriter writer, ScopeAccountingSet value)
    {
        writer.WriteStartObject();
        writer.WriteString("axis", AxisName(value.Axis));
        writer.WriteString("disposition", DispositionName(value.Disposition));
        WriteIntArray(writer, "object_ordinals", value.ObjectOrdinals);
        writer.WriteEndObject();
    }

    private static void WriteExpandedRow(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        SourceObjectRef objectRef,
        string objectRefSha256,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        IReadOnlyList<ScopeRuleEvaluation> evaluations,
        IReadOnlyList<ScopeAxisResult> axisResults)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("object_ref");
        WriteObjectRef(writer, objectRef);
        writer.WriteString("object_ref_sha256", objectRefSha256);
        writer.WritePropertyName("selectors");
        writer.WriteStartArray();
        for (var index = 0; index < selectors.Count; index++)
        {
            WriteExpandedSelector(writer, profile, evidenceArtifacts, index, selectors[index]);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("rule_evaluations");
        writer.WriteStartArray();
        foreach (var evaluation in evaluations)
        {
            WriteExpandedEvaluation(writer, profile, evaluation);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("axis_results");
        writer.WriteStartArray();
        foreach (var result in axisResults)
        {
            writer.WriteStartObject();
            writer.WriteString("axis", AxisName(result.Axis));
            writer.WritePropertyName("winning_rule");
            WriteMember(
                writer,
                profile.OrderedMembers[
                    profile.OrderedRules[result.WinningRuleOrdinal].RuleMemberOrdinal]);
            writer.WriteNumber("winning_ordinal", result.WinningRuleOrdinal);
            writer.WriteString("effect", EffectName(result.Effect));
            writer.WriteString("disposition", DispositionName(result.Disposition));
            WriteResolvedMembers(
                writer,
                "roles",
                profile,
                result.RoleMemberOrdinals);
            WriteResolvedMembers(
                writer,
                "capabilities",
                profile,
                result.CapabilityMemberOrdinals);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteExpandedSelector(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        int selectorOrdinal,
        ScopeSelectorEvidence selector)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("selector");
        WriteMember(
            writer,
            profile.OrderedMembers[profile.OrderedSelectorMemberOrdinals[selectorOrdinal]]);
        writer.WriteString("state", SelectorStateName(selector.State));
        writer.WritePropertyName("canonical_values");
        writer.WriteStartArray();
        foreach (var item in selector.CanonicalValues)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("evidence_artifact_ref");
        if (selector.EvidenceArtifactOrdinal is { } evidenceOrdinal)
        {
            WriteArtifact(writer, evidenceArtifacts[evidenceOrdinal]);
        }
        else
        {
            writer.WriteNullValue();
        }

        WriteNullableString(
            writer,
            "evidence_kind",
            selector.EvidenceKind is null
                ? null
                : EvidenceKindName(selector.EvidenceKind.Value));
        writer.WritePropertyName("rule_ref");
        if (selector.RuleOrdinal is { } ruleOrdinal)
        {
            WriteMember(
                writer,
                profile.OrderedMembers[profile.OrderedRules[ruleOrdinal].RuleMemberOrdinal]);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName("cause_ref");
        if (selector.CauseMemberOrdinal is { } causeOrdinal)
        {
            WriteMember(writer, profile.OrderedMembers[causeOrdinal]);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
    }

    private static void WriteExpandedEvaluation(
        Utf8JsonWriter writer,
        ScopeProfileBinding profile,
        ScopeRuleEvaluation evaluation)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("rule");
        WriteMember(
            writer,
            profile.OrderedMembers[
                profile.OrderedRules[evaluation.RuleOrdinal].RuleMemberOrdinal]);
        writer.WriteNumber("ordinal", evaluation.RuleOrdinal);
        writer.WriteString("state", EvaluationStateName(evaluation.State));
        WriteNullableString(
            writer,
            "effect",
            evaluation.Effect is null ? null : EffectName(evaluation.Effect.Value));
        WriteNullableString(
            writer,
            "disposition",
            evaluation.Disposition is null
                ? null
                : DispositionName(evaluation.Disposition.Value));
        WriteResolvedMembers(
            writer,
            "roles",
            profile,
            evaluation.RoleMemberOrdinals);
        WriteResolvedMembers(
            writer,
            "capabilities",
            profile,
            evaluation.CapabilityMemberOrdinals);
        writer.WriteEndObject();
    }

    private static void WriteResolvedMembers(
        Utf8JsonWriter writer,
        string name,
        ScopeProfileBinding profile,
        IReadOnlyList<int> ordinals)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var ordinal in ordinals)
        {
            WriteMember(writer, profile.OrderedMembers[ordinal]);
        }

        writer.WriteEndArray();
    }

    internal static void WriteObjectRef(Utf8JsonWriter writer, SourceObjectRef value)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", value.Schema);
        writer.WriteString("authority", value.Authority switch
        {
            SourceAuthority.Jolux => "jolux",
            SourceAuthority.Cellar => "cellar",
            _ => throw new InvalidOperationException("Unknown source authority."),
        });
        writer.WritePropertyName("entity_kind");
        WriteMember(writer, value.EntityKind);
        writer.WriteString("publisher_uri", value.PublisherUri);
        writer.WriteString("canonical_key", value.CanonicalKey);
        writer.WriteString("canonical_key_sha256", value.CanonicalKeySha256);
        writer.WritePropertyName("identity_profile_ref");
        WriteArtifact(writer, value.IdentityProfileRef);
        writer.WritePropertyName("parent_key_ref");
        if (value.ParentKeyRef is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            WriteObjectKeyRef(writer, value.ParentKeyRef);
        }

        writer.WriteEndObject();
    }

    private static void WriteObjectKeyRef(Utf8JsonWriter writer, SourceObjectKeyRef value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("entity_kind");
        WriteMember(writer, value.EntityKind);
        writer.WriteString("publisher_uri", value.PublisherUri);
        writer.WriteString("canonical_key", value.CanonicalKey);
        writer.WriteString("canonical_key_sha256", value.CanonicalKeySha256);
        writer.WriteEndObject();
    }

    internal static void WriteMember(Utf8JsonWriter writer, SourceRegistryMemberRef value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("registry_ref");
        WriteArtifact(writer, value.RegistryRef);
        writer.WriteString("member_key", value.MemberKey);
        writer.WriteEndObject();
    }

    internal static void WriteArtifact(Utf8JsonWriter writer, SourceArtifactRef value)
    {
        writer.WriteStartObject();
        writer.WriteString("resource_id", value.ResourceId);
        writer.WriteString("sha256", value.Sha256);
        writer.WriteEndObject();
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<T> values,
        Action<Utf8JsonWriter, T> write)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            write(writer, value);
        }

        writer.WriteEndArray();
    }

    private static void WriteIntArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<int> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableInt(Utf8JsonWriter writer, string name, int? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    internal static string AxisName(ScopeAxis value) => value switch
    {
        ScopeAxis.Record => "record",
        ScopeAxis.Body => "body",
        ScopeAxis.Relation => "relation",
        ScopeAxis.SupportingDocument => "supporting_document",
        _ => throw new InvalidOperationException("Unknown scope axis."),
    };

    internal static string SelectorStateName(ScopeSelectorState value) => value switch
    {
        ScopeSelectorState.PublisherValuePresent => "publisher_value_present",
        ScopeSelectorState.PublisherValueAbsent => "publisher_value_absent",
        ScopeSelectorState.PublisherValueConflict => "publisher_value_conflict",
        ScopeSelectorState.SelectorNotApplicable => "selector_not_applicable",
        _ => throw new InvalidOperationException("Unknown selector state."),
    };

    internal static string EvidenceKindName(ScopeSelectorEvidenceKind value) => value switch
    {
        ScopeSelectorEvidenceKind.ObservedValueSet => "observed_value_set",
        ScopeSelectorEvidenceKind.CompleteObservationAbsence =>
            "complete_observation_absence",
        ScopeSelectorEvidenceKind.ObservedConflictingValueSet =>
            "observed_conflicting_value_set",
        _ => throw new InvalidOperationException("Unknown selector evidence kind."),
    };

    internal static string EvaluationStateName(ScopeRuleEvaluationState value) => value switch
    {
        ScopeRuleEvaluationState.NotMatched => "not_matched",
        ScopeRuleEvaluationState.Matched => "matched",
        _ => throw new InvalidOperationException("Unknown rule evaluation state."),
    };

    internal static string EffectName(ScopeRuleEffect value) => value switch
    {
        ScopeRuleEffect.Positive => "positive",
        ScopeRuleEffect.ExactDenial => "exact_denial",
        _ => throw new InvalidOperationException("Unknown rule effect."),
    };

    internal static string DispositionName(ScopeDisposition value) => value switch
    {
        ScopeDisposition.AcceptedSelected => "accepted_selected",
        ScopeDisposition.TypedQuarantine => "typed_quarantine",
        ScopeDisposition.Point => "point",
        ScopeDisposition.NeverIngest => "never_ingest",
        _ => throw new InvalidOperationException("Unknown scope disposition."),
    };

    private static void WriteAscii(Stream output, string value)
    {
        Span<byte> bytes = stackalloc byte[value.Length];
        WriteAscii(bytes, value);
        output.Write(bytes);
    }

    private static void WriteAscii(Span<byte> target, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] > 0x7f)
            {
                throw new InvalidOperationException("A canonical domain contains Unicode.");
            }

            target[index] = (byte)value[index];
        }
    }

    private sealed class HashingWriteStream : Stream
    {
        private readonly Stream _destination;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;

        public HashingWriteStream(Stream destination, string hashDomain)
        {
            _destination = destination;
            Span<byte> bytes = stackalloc byte[hashDomain.Length];
            WriteAscii(bytes, hashDomain);
            _hash.AppendData(bytes);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public long BytesWritten { get; private set; }

        public string GetHashAndReset()
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _completed = true;
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            _hash.GetHashAndReset(digest);
            return Convert.ToHexStringLower(digest);
        }

        public override void Flush() => _destination.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _hash.AppendData(buffer);
            _destination.Write(buffer);
            BytesWritten = checked(BytesWritten + buffer.Length);
        }

        public override void WriteByte(byte value)
        {
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = value;
            Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal static class ScopeValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static readonly ScopeAxis[] AllAxes = Enum.GetValues<ScopeAxis>();
    internal static readonly ScopeDisposition[] AllDispositions = Enum.GetValues<ScopeDisposition>();

    public static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum => SourceCoreValidation.RequireDefined(value, parameterName);

    public static int RequireOrdinal(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    public static int? RequireNullableOrdinal(int? value, string parameterName) =>
        value is null ? null : RequireOrdinal(value.Value, parameterName);

    public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentException("Contract arrays cannot contain null members.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<T> CopyNonempty<T>(
        IReadOnlyList<T> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName);
        if (copy.Count == 0)
        {
            throw new ArgumentException("The contract array cannot be empty.", parameterName);
        }

        return copy;
    }

    public static IReadOnlyList<int> CopyOrdinals(
        IReadOnlyList<int> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName);
        foreach (var value in copy)
        {
            RequireOrdinal(value, parameterName);
        }

        return copy;
    }

    public static IReadOnlyList<int> CopySortedOrdinals(
        IReadOnlyList<int> values,
        string parameterName)
    {
        var copy = CopyOrdinals(values, parameterName);
        for (var index = 1; index < copy.Count; index++)
        {
            if (copy[index - 1] >= copy[index])
            {
                throw new ArgumentException(
                    "Ordinal arrays must be strictly increasing and unique.",
                    parameterName);
            }
        }

        return copy;
    }

    public static IReadOnlyList<int> CopyUniqueOrdinals(
        IReadOnlyList<int> values,
        string parameterName)
    {
        var copy = CopyOrdinals(values, parameterName);
        if (copy.Distinct().Count() != copy.Count)
        {
            throw new ArgumentException("Ordinal arrays must be unique.", parameterName);
        }

        return copy;
    }

    public static IReadOnlyList<string> CopyCanonicalValues(
        IReadOnlyList<string> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName);
        for (var index = 0; index < copy.Count; index++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(copy[index], parameterName);
            try
            {
                _ = StrictUtf8.GetByteCount(copy[index]);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Canonical selector values must contain valid Unicode scalar values.",
                    parameterName,
                    exception);
            }

            if (copy[index].EnumerateRunes().Count() > 4096 ||
                (index > 0 && CompareUtf8(copy[index - 1], copy[index]) >= 0))
            {
                throw new ArgumentException(
                    "Canonical selector values must contain at most 4096 Unicode scalar values, " +
                    "be UTF-8-sorted, and be unique.",
                    parameterName);
            }
        }

        return copy;
    }

    public static IReadOnlyList<SourceRegistryMemberRef> CopySortedMembers(
        IReadOnlyList<SourceRegistryMemberRef> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName);
        for (var index = 1; index < copy.Count; index++)
        {
            if (CompareMember(copy[index - 1], copy[index]) >= 0)
            {
                throw new ArgumentException(
                    "Member tables must be canonically sorted and unique.",
                    parameterName);
            }
        }

        return copy;
    }

    public static IReadOnlyList<SourceArtifactRef> CopySortedArtifacts(
        IReadOnlyList<SourceArtifactRef> values,
        string parameterName)
    {
        var copy = Copy(values, parameterName);
        for (var index = 1; index < copy.Count; index++)
        {
            if (CompareArtifact(copy[index - 1], copy[index]) >= 0)
            {
                throw new ArgumentException(
                    "Evidence artifacts must be canonically sorted and unique.",
                    parameterName);
            }
        }

        return copy;
    }

    public static int RequireMemberOrdinal(
        int ordinal,
        IReadOnlyList<SourceRegistryMemberRef> members,
        SourceArtifactRef expectedRegistry,
        string parameterName)
    {
        RequireOrdinal(ordinal, parameterName);
        if (ordinal >= members.Count ||
            !ArtifactEquals(members[ordinal].RegistryRef, expectedRegistry))
        {
            throw new ArgumentException(
                "A member ordinal does not resolve in the required registry.",
                parameterName);
        }

        return ordinal;
    }

    public static void RequireOutcomeMembers(
        ScopeRuleEffect effect,
        ScopeDisposition disposition,
        IReadOnlyList<int> roleMemberOrdinals,
        IReadOnlyList<int> capabilityMemberOrdinals,
        string parameterName)
    {
        if (effect == ScopeRuleEffect.ExactDenial &&
            disposition == ScopeDisposition.AcceptedSelected)
        {
            throw new ArgumentException(
                "An exact denial cannot produce an accepted selection.",
                parameterName);
        }

        if (disposition != ScopeDisposition.AcceptedSelected &&
            (roleMemberOrdinals.Count != 0 || capabilityMemberOrdinals.Count != 0))
        {
            throw new ArgumentException(
                "Only an accepted selection may carry roles or capabilities.",
                parameterName);
        }
    }

    public static string RequireBase64Url(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(static character =>
                character is not ((>= 'A' and <= 'Z') or
                    (>= 'a' and <= 'z') or
                    (>= '0' and <= '9') or '-' or '_')))
        {
            throw new ArgumentException(
                "Rule-match bits must use unpadded Base64url.",
                parameterName);
        }

        return value;
    }

    public static bool ArtifactEquals(SourceArtifactRef left, SourceArtifactRef right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.Ordinal) &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal);

    public static bool MemberEquals(
        SourceRegistryMemberRef left,
        SourceRegistryMemberRef right) =>
        ArtifactEquals(left.RegistryRef, right.RegistryRef) &&
        string.Equals(left.MemberKey, right.MemberKey, StringComparison.Ordinal);

    public static int CompareArtifact(SourceArtifactRef left, SourceArtifactRef right)
    {
        // Both fields are constructor-validated ASCII. Ordinal comparison is therefore
        // byte-for-byte equivalent to unsigned UTF-8 ordering without allocating two
        // temporary byte arrays for every table comparison.
        var comparison = string.CompareOrdinal(left.ResourceId, right.ResourceId);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Sha256, right.Sha256);
    }

    public static int CompareMember(SourceRegistryMemberRef left, SourceRegistryMemberRef right)
    {
        var comparison = CompareArtifact(left.RegistryRef, right.RegistryRef);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.MemberKey, right.MemberKey);
    }

    public static int CompareUtf8(string left, string right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        var leftBytes = StrictUtf8.GetBytes(left);
        var rightBytes = StrictUtf8.GetBytes(right);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }
}

internal static class ScopeRuleBits
{
    public static string Encode(IReadOnlyList<ScopeRuleEvaluation> evaluations)
    {
        var bytes = new byte[(evaluations.Count + 7) / 8];
        foreach (var evaluation in evaluations)
        {
            if (evaluation.State == ScopeRuleEvaluationState.Matched)
            {
                bytes[evaluation.RuleOrdinal / 8] |= (byte)(1 << (evaluation.RuleOrdinal % 8));
            }
        }

        return EncodeBytes(bytes);
    }

    public static bool[] Decode(string value, int ruleCount)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ruleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleCount));
        }

        var expectedLength = (ruleCount + 7) / 8;
        var expectedEncodedLength = ((expectedLength * 8) + 5) / 6;
        if (value.Length != expectedEncodedLength)
        {
            throw new InvalidOperationException(
                "The rule-match bit vector has a noncanonical length.");
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new InvalidOperationException("The rule-match bit vector is malformed."),
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The rule-match bit vector is malformed.", exception);
        }

        if (bytes.Length != expectedLength ||
            !string.Equals(EncodeBytes(bytes), value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The rule-match bit vector has a noncanonical length or spelling.");
        }

        var remainder = ruleCount % 8;
        if (remainder != 0 && (bytes[^1] >> remainder) != 0)
        {
            throw new InvalidOperationException("Unused rule-match bits must be zero.");
        }

        var result = new bool[ruleCount];
        for (var index = 0; index < ruleCount; index++)
        {
            result[index] = (bytes[index / 8] & (1 << (index % 8))) != 0;
        }

        return result;
    }

    private static string EncodeBytes(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

internal sealed class ScopeObservedObjectComparer :
    IComparer<ScopeObservedObjectEntry>,
    IEqualityComparer<SourceObjectRef>
{
    public static ScopeObservedObjectComparer Instance { get; } = new();

    public int Compare(ScopeObservedObjectEntry? left, ScopeObservedObjectEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        // The digest is fixed lowercase ASCII, so ordinal comparison is the exact
        // portable UTF-8 wire order and keeps the 555k-object sort allocation-free.
        var digest = string.CompareOrdinal(left.ObjectRefSha256, right.ObjectRefSha256);
        return digest != 0 ? digest : CompareObjects(left.ObjectRef, right.ObjectRef);
    }

    public bool Equals(SourceObjectRef? left, SourceObjectRef? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && CompareObjects(left, right) == 0;

    public int GetHashCode(SourceObjectRef value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = new HashCode();
        hash.Add(value.Schema, StringComparer.Ordinal);
        hash.Add(value.Authority);
        AddMember(ref hash, value.EntityKind);
        hash.Add(value.PublisherUri, StringComparer.Ordinal);
        hash.Add(value.CanonicalKey, StringComparer.Ordinal);
        hash.Add(value.CanonicalKeySha256, StringComparer.Ordinal);
        AddArtifact(ref hash, value.IdentityProfileRef);
        if (value.ParentKeyRef is { } parent)
        {
            AddMember(ref hash, parent.EntityKind);
            hash.Add(parent.PublisherUri, StringComparer.Ordinal);
            hash.Add(parent.CanonicalKey, StringComparer.Ordinal);
            hash.Add(parent.CanonicalKeySha256, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static int CompareObjects(SourceObjectRef left, SourceObjectRef right)
    {
        var comparison = string.CompareOrdinal(left.Schema, right.Schema);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Authority.CompareTo(right.Authority);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = ScopeValidation.CompareMember(left.EntityKind, right.EntityKind);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.PublisherUri, right.PublisherUri);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = ScopeValidation.CompareUtf8(left.CanonicalKey, right.CanonicalKey);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.CanonicalKeySha256, right.CanonicalKeySha256);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = ScopeValidation.CompareArtifact(
            left.IdentityProfileRef,
            right.IdentityProfileRef);
        if (comparison != 0)
        {
            return comparison;
        }

        return CompareParents(left.ParentKeyRef, right.ParentKeyRef);
    }

    private static int CompareParents(SourceObjectKeyRef? left, SourceObjectKeyRef? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = ScopeValidation.CompareMember(left.EntityKind, right.EntityKind);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.PublisherUri, right.PublisherUri);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = ScopeValidation.CompareUtf8(left.CanonicalKey, right.CanonicalKey);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.CanonicalKeySha256, right.CanonicalKeySha256);
    }

    private static void AddArtifact(ref HashCode hash, SourceArtifactRef value)
    {
        hash.Add(value.ResourceId, StringComparer.Ordinal);
        hash.Add(value.Sha256, StringComparer.Ordinal);
    }

    private static void AddMember(ref HashCode hash, SourceRegistryMemberRef value)
    {
        AddArtifact(ref hash, value.RegistryRef);
        hash.Add(value.MemberKey, StringComparer.Ordinal);
    }
}
