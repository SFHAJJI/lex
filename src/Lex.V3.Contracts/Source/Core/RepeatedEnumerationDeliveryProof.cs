using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Core;

public enum RepeatedEnumerationThresholdAssessment { BelowMaximum = 1, PartitionRequired = 2 }
public enum EnumerationDeliveryOutcome { EqualSelections = 1, DifferentSelections = 2 }
public enum RepeatedEnumerationRdfTermKind { Iri = 1, BlankNode = 2, Literal = 3, Unbound = 4 }
public enum RepeatedEnumerationSparqlJsonDialect { LuxembourgVirtuoso = 1, EuropeanUnionVirtuoso = 2 }

public static class EnumerationCursorEnvelope
{
    public const string Identity = "h-prefixed-utf8-lowercase-hex/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "h" + Convert.ToHexStringLower(StrictUtf8.GetBytes(value));
    }
    public static string Decode(string encoded)
    {
        ArgumentException.ThrowIfNullOrEmpty(encoded);
        var payload = encoded.AsSpan(1);
        if (encoded[0] != 'h' || payload.Length % 2 != 0 || payload.ContainsAnyExcept("0123456789abcdef")) throw new ArgumentException("A cursor envelope must be h-prefixed lowercase hexadecimal.", nameof(encoded));
        try
        {
            var decoded = StrictUtf8.GetString(Convert.FromHexString(payload));
            if (!string.Equals(encoded, Encode(decoded), StringComparison.Ordinal)) throw new FormatException("The cursor envelope is not canonical.");
            return decoded;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException) { throw new ArgumentException("A cursor envelope must contain valid UTF-8.", nameof(encoded), exception); }
    }

    public static int CompareRaw(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return StrictUtf8.GetBytes(left).AsSpan().SequenceCompareTo(StrictUtf8.GetBytes(right));
    }
}

public sealed record RepeatedEnumerationRdfTerm
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private RepeatedEnumerationRdfTerm(RepeatedEnumerationRdfTermKind kind, string? value, string? datatype, string? language)
    {
        Kind = SourceCoreValidation.RequireDefined(kind, nameof(kind));
        Value = Text(value, nameof(value));
        Datatype = Text(datatype, nameof(datatype));
        Language = Text(language, nameof(language));
        if (kind == RepeatedEnumerationRdfTermKind.Unbound ? Value is not null || Datatype is not null || Language is not null : Value is null)
            throw new ArgumentException("The RDF term shape is invalid.");
        if (kind != RepeatedEnumerationRdfTermKind.Literal && (Datatype is not null || Language is not null) || Datatype is not null && Language is not null)
            throw new ArgumentException("Only one literal datatype or language qualifier is allowed.");
    }
    public RepeatedEnumerationRdfTermKind Kind { get; }
    public string? Value { get; }
    public string? Datatype { get; }
    public string? Language { get; }
    public static RepeatedEnumerationRdfTerm Iri(string value) => new(RepeatedEnumerationRdfTermKind.Iri, value, null, null);
    public static RepeatedEnumerationRdfTerm BlankNode(string value) => new(RepeatedEnumerationRdfTermKind.BlankNode, value, null, null);
    public static RepeatedEnumerationRdfTerm Literal(string value, string? datatype, string? language) => new(RepeatedEnumerationRdfTermKind.Literal, value, datatype, language);
    public static RepeatedEnumerationRdfTerm Unbound() => new(RepeatedEnumerationRdfTermKind.Unbound, null, null, null);
    private static string? Text(string? value, string name)
    {
        if (value is null) return null;
        try { if (StrictUtf8.GetByteCount(value) > 4 * 1024 * 1024) throw new ArgumentException("RDF text is too large.", name); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("RDF text is not valid Unicode.", name, exception); }
        return value;
    }
}

public sealed record RepeatedEnumerationRow(IReadOnlyList<RepeatedEnumerationRdfTerm> Terms, IReadOnlyList<RepeatedEnumerationRdfTerm> CanonicalKey, IReadOnlyList<RepeatedEnumerationRdfTerm> Cursor);

public sealed record RepeatedEnumerationInterpretationProfile
{
    public const string SchemaId = "repeated_enumeration_sparql_json_profile/1";
    public RepeatedEnumerationInterpretationProfile(string schema, RepeatedEnumerationSparqlJsonDialect dialect, string expectedMediaType, string cursorEnvelopeIdentity, long maximumDeliverableRows, string thresholdDetectorIdentity, SourceRegistryMemberRef countQueryFamilyRef, SourceRegistryMemberRef pageQueryFamilyRef, string countVariable, IReadOnlyList<string> projectionVariables, IReadOnlyList<string> canonicalKeyVariables, IReadOnlyList<string> cursorVariables, IReadOnlyList<string> selectionParameterNames, string passParameterName, IReadOnlyList<string> cursorParameterNames, string hasCursorParameterName)
    {
        if (schema != SchemaId) throw new ArgumentException($"The profile must declare {SchemaId}.", nameof(schema));
        Schema = schema;
        Dialect = SourceCoreValidation.RequireDefined(dialect, nameof(dialect));
        if (expectedMediaType != "application/sparql-results+json") throw new ArgumentException("The strict profile admits only SPARQL Results JSON.", nameof(expectedMediaType));
        ExpectedMediaType = expectedMediaType;
        if (cursorEnvelopeIdentity != EnumerationCursorEnvelope.Identity) throw new ArgumentException("The cursor envelope identity is unsupported.", nameof(cursorEnvelopeIdentity));
        CursorEnvelopeIdentity = cursorEnvelopeIdentity;
        if (maximumDeliverableRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDeliverableRows));
        MaximumDeliverableRows = maximumDeliverableRows;
        if (thresholdDetectorIdentity != "enumeration-row-threshold/1") throw new ArgumentException("The threshold detector identity is unsupported.", nameof(thresholdDetectorIdentity));
        ThresholdDetectorIdentity = thresholdDetectorIdentity;
        CountQueryFamilyRef = countQueryFamilyRef ?? throw new ArgumentNullException(nameof(countQueryFamilyRef));
        PageQueryFamilyRef = pageQueryFamilyRef ?? throw new ArgumentNullException(nameof(pageQueryFamilyRef));
        CountVariable = Name(countVariable, nameof(countVariable));
        ProjectionVariables = Names(projectionVariables, nameof(projectionVariables));
        CanonicalKeyVariables = Subset(canonicalKeyVariables, ProjectionVariables, nameof(canonicalKeyVariables));
        CursorVariables = Subset(cursorVariables, ProjectionVariables, nameof(cursorVariables));
        SelectionParameterNames = Names(selectionParameterNames, nameof(selectionParameterNames), allowEmpty: true);
        PassParameterName = Name(passParameterName, nameof(passParameterName));
        CursorParameterNames = Names(cursorParameterNames, nameof(cursorParameterNames));
        HasCursorParameterName = Name(hasCursorParameterName, nameof(hasCursorParameterName));
        if (CursorVariables.Count != CursorParameterNames.Count)
            throw new ArgumentException("Cursor variables and input parameters must have equal arity.");
        if (SelectionParameterNames.Intersect(CursorParameterNames, StringComparer.Ordinal).Any() ||
            SelectionParameterNames.Contains(PassParameterName, StringComparer.Ordinal) ||
            SelectionParameterNames.Contains(HasCursorParameterName, StringComparer.Ordinal) ||
            CursorParameterNames.Contains(PassParameterName, StringComparer.Ordinal) ||
            CursorParameterNames.Contains(HasCursorParameterName, StringComparer.Ordinal) ||
            PassParameterName == HasCursorParameterName)
            throw new ArgumentException("Selection, pass, cursor and page-state parameter names must be disjoint.");
    }
    public string Schema { get; }
    public RepeatedEnumerationSparqlJsonDialect Dialect { get; }
    public string ExpectedMediaType { get; }
    public string CursorEnvelopeIdentity { get; }
    public long MaximumDeliverableRows { get; }
    public string ThresholdDetectorIdentity { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountVariable { get; }
    public IReadOnlyList<string> ProjectionVariables { get; }
    public IReadOnlyList<string> CanonicalKeyVariables { get; }
    public IReadOnlyList<string> CursorVariables { get; }
    public IReadOnlyList<string> SelectionParameterNames { get; }
    public string PassParameterName { get; }
    public IReadOnlyList<string> CursorParameterNames { get; }
    public string HasCursorParameterName { get; }
    private static string Name(string value, string name) => MachineQueryValidation.RequireMachineMemberKey(value, name);
    private static IReadOnlyList<string> Names(IReadOnlyList<string> source, string name, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(source, name);
        var values = source.Select((value, index) => Name(value, $"{name}[{index}]")).ToArray();
        if ((!allowEmpty && values.Length == 0) || values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new ArgumentException("Names must be unique and bounded.", name);
        return Array.AsReadOnly(values);
    }
    private static IReadOnlyList<string> Subset(IReadOnlyList<string> source, IReadOnlyList<string> projection, string name)
    {
        var values = Names(source, name);
        if (values.Any(value => !projection.Contains(value, StringComparer.Ordinal))) throw new ArgumentException("Variables must belong to the projection.", name);
        return values;
    }
}

public static class RepeatedEnumerationInterpretationProfileIdentity
{
    public const string CanonicalizationIdentity = "repeated-enumeration-sparql-json-profile-canonical-json/1";
    public static SourceArtifactRef Create(string resourceId, RepeatedEnumerationInterpretationProfile profile) => new(resourceId, MachineQueryValidation.Sha256(ContractCanonicalizer.Canonicalize(profile, CanonicalizationIdentity, 64)));
    public static void Validate(SourceArtifactRef reference, RepeatedEnumerationInterpretationProfile profile) { if (reference != Create(reference.ResourceId, profile)) throw new ArgumentException("The interpretation profile reference does not bind.", nameof(reference)); }
}

public sealed record RepeatedEnumerationEvidenceRefs(SourceArtifactRef QueryPlanRef, SourceArtifactRef QueryInputRef, SourceArtifactRef RenderReceiptRef, SourceArtifactRef RequestEvidenceRef, SourceArtifactRef ObservationRef);
public sealed record RepeatedEnumerationPageRef(int Ordinal, RepeatedEnumerationEvidenceRefs Evidence);
public sealed record EnumerationPageSetRefs(IReadOnlyList<RepeatedEnumerationPageRef> Pages);
public sealed record RepeatedEnumerationResolvedEvidence(MachineQueryPlan QueryPlan, MachineQueryInputArtifact QueryInput, MachineQueryRenderReceipt RenderReceipt, IMachineQueryRenderer Renderer, MachineRequestEvidence RequestEvidence, ResponseCompleteBodyObservation Observation, ReadOnlyMemory<byte> RetainedPayloadBytes);
public sealed record EnumerationObservationTimes(string CountA, IReadOnlyList<string> PagesA, string CountB, IReadOnlyList<string> PagesB);
public interface IRepeatedEnumerationEvidenceResolver { RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs references); }

public static class MachineRequestEvidenceIdentity
{
    public const string CanonicalizationIdentity = "machine-request-evidence-canonical-json/1";
    public static SourceArtifactRef Create(string resourceId, MachineRequestEvidence evidence) => new(resourceId, MachineQueryValidation.Sha256(ContractCanonicalizer.Canonicalize(evidence, CanonicalizationIdentity, 128)));
    public static void Validate(SourceArtifactRef reference, MachineRequestEvidence evidence) { if (reference != Create(reference.ResourceId, evidence)) throw new ArgumentException("The request evidence reference does not bind.", nameof(reference)); }
}

public sealed class EnumerationDeliveryComparison
{
    private EnumerationDeliveryComparison(SourceArtifactRef profileRef, RepeatedEnumerationThresholdAssessment thresholdAssessment, RepeatedEnumerationEvidenceRefs countA, EnumerationPageSetRefs pagesA, RepeatedEnumerationEvidenceRefs countB, EnumerationPageSetRefs pagesB, EnumerationObservationTimes observationTimes, long selectedA, long selectedB, IReadOnlyList<RepeatedEnumerationRow> rowsA, IReadOnlyList<RepeatedEnumerationRow> rowsB)
    {
        InterpretationProfileRef = profileRef; CountA = countA; PagesA = Snapshot(pagesA); CountB = countB; PagesB = Snapshot(pagesB);
        ThresholdAssessment = thresholdAssessment;
        ObservationTimes = new(observationTimes.CountA, Array.AsReadOnly(observationTimes.PagesA.ToArray()), observationTimes.CountB, Array.AsReadOnly(observationTimes.PagesB.ToArray()));
        SelectedRowCountA = selectedA; SelectedRowCountB = selectedB; DeliveredRowCountA = rowsA.Count; DeliveredRowCountB = rowsB.Count;
        CanonicalRowDigestA = Digest("repeated_enumeration_rows/1", rowsA.Select(static row => row.Terms)); CanonicalRowDigestB = Digest("repeated_enumeration_rows/1", rowsB.Select(static row => row.Terms));
        CanonicalKeyDigestA = Digest("repeated_enumeration_keys/1", rowsA.Select(static row => row.CanonicalKey)); CanonicalKeyDigestB = Digest("repeated_enumeration_keys/1", rowsB.Select(static row => row.CanonicalKey));
        CursorDigestA = Digest("repeated_enumeration_cursors/1", rowsA.Select(static row => row.Cursor)); CursorDigestB = Digest("repeated_enumeration_cursors/1", rowsB.Select(static row => row.Cursor));
        Outcome = ClassifyOutcome(
            selectedA,
            selectedB,
            rowsA.Count,
            rowsB.Count,
            CanonicalRowDigestA,
            CanonicalRowDigestB,
            CanonicalKeyDigestA,
            CanonicalKeyDigestB,
            CursorDigestA,
            CursorDigestB);
    }
    public SourceArtifactRef InterpretationProfileRef { get; }
    public RepeatedEnumerationThresholdAssessment ThresholdAssessment { get; }
    public RepeatedEnumerationEvidenceRefs CountA { get; }
    public EnumerationPageSetRefs PagesA { get; }
    public RepeatedEnumerationEvidenceRefs CountB { get; }
    public EnumerationPageSetRefs PagesB { get; }
    public EnumerationObservationTimes ObservationTimes { get; }
    public long SelectedRowCountA { get; }
    public long SelectedRowCountB { get; }
    public long DeliveredRowCountA { get; }
    public long DeliveredRowCountB { get; }
    public string CanonicalRowDigestA { get; }
    public string CanonicalRowDigestB { get; }
    public string CanonicalKeyDigestA { get; }
    public string CanonicalKeyDigestB { get; }
    public string CursorDigestA { get; }
    public string CursorDigestB { get; }
    public EnumerationDeliveryOutcome Outcome { get; }

    public static EnumerationDeliveryComparison Create(RepeatedEnumerationInterpretationProfile profile, SourceArtifactRef profileRef, RepeatedEnumerationEvidenceRefs countA, EnumerationPageSetRefs pagesA, RepeatedEnumerationEvidenceRefs countB, EnumerationPageSetRefs pagesB, IRepeatedEnumerationEvidenceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(resolver); RepeatedEnumerationInterpretationProfileIdentity.Validate(profileRef, profile);
        var frozenPagesA = Snapshot(pagesA); var frozenPagesB = Snapshot(pagesB);
        var aCount = Resolve(countA, profile, profile.CountQueryFamilyRef, resolver); var aPages = ResolvePages(frozenPagesA, profile, resolver);
        var bCount = Resolve(countB, profile, profile.CountQueryFamilyRef, resolver); var bPages = ResolvePages(frozenPagesB, profile, resolver);
        RequireDistinct(countA, frozenPagesA, countB, frozenPagesB);
        var evidenceA = new[] { aCount }.Concat(aPages).ToArray();
        var evidenceB = new[] { bCount }.Concat(bPages).ToArray();
        RequireSameSelection(evidenceA.Concat(evidenceB), profile);
        RequireSamePartition(evidenceA.Concat(evidenceB));
        RequireDistinctPasses(evidenceA, evidenceB, profile);
        RequireDifferentPageLimits(aPages, bPages);
        var selectedA = ParseCount(aCount.RetainedPayloadBytes.Span, profile); var selectedB = ParseCount(bCount.RetainedPayloadBytes.Span, profile);
        var rowsA = VerifyPages(aPages, countA.ObservationRef, selectedA, profile); var rowsB = VerifyPages(bPages, countB.ObservationRef, selectedB, profile);
        var times = new EnumerationObservationTimes(aCount.Observation.Request.ObservedAtUtc, aPages.Select(static page => page.Observation.Request.ObservedAtUtc).ToArray(), bCount.Observation.Request.ObservedAtUtc, bPages.Select(static page => page.Observation.Request.ObservedAtUtc).ToArray());
        return new(profileRef, AssessThreshold(Math.Max(selectedA, selectedB), profile), countA, frozenPagesA, countB, frozenPagesB, times, selectedA, selectedB, rowsA, rowsB);
    }
    public static RepeatedEnumerationThresholdAssessment AssessThreshold(long count, RepeatedEnumerationInterpretationProfile profile) { if (count < 0) throw new ArgumentOutOfRangeException(nameof(count)); ArgumentNullException.ThrowIfNull(profile); return count < profile.MaximumDeliverableRows ? RepeatedEnumerationThresholdAssessment.BelowMaximum : RepeatedEnumerationThresholdAssessment.PartitionRequired; }

    internal static EnumerationDeliveryOutcome ClassifyOutcome(
        long selectedA,
        long selectedB,
        long deliveredA,
        long deliveredB,
        string canonicalRowDigestA,
        string canonicalRowDigestB,
        string canonicalKeyDigestA,
        string canonicalKeyDigestB,
        string cursorDigestA,
        string cursorDigestB) =>
        selectedA == selectedB &&
        selectedA == deliveredA &&
        selectedB == deliveredB &&
        string.Equals(canonicalRowDigestA, canonicalRowDigestB, StringComparison.Ordinal) &&
        string.Equals(canonicalKeyDigestA, canonicalKeyDigestB, StringComparison.Ordinal) &&
        string.Equals(cursorDigestA, cursorDigestB, StringComparison.Ordinal)
            ? EnumerationDeliveryOutcome.EqualSelections
            : EnumerationDeliveryOutcome.DifferentSelections;

    internal static void RequireContinuation(
        long hasCursor,
        IReadOnlyList<string> actualCursor,
        IReadOnlyList<string> expectedCursor,
        long previousPageCount,
        long rowLimit)
    {
        ArgumentNullException.ThrowIfNull(actualCursor);
        ArgumentNullException.ThrowIfNull(expectedCursor);
        if (hasCursor != 1 ||
            !actualCursor.SequenceEqual(expectedCursor, StringComparer.Ordinal) ||
            previousPageCount != rowLimit)
        {
            throw new ArgumentException("The typed cursor continuation is invalid.");
        }
    }

    private static RepeatedEnumerationResolvedEvidence Resolve(RepeatedEnumerationEvidenceRefs refs, RepeatedEnumerationInterpretationProfile profile, SourceRegistryMemberRef family, IRepeatedEnumerationEvidenceResolver resolver)
    {
        var value = resolver.Resolve(refs) ?? throw new ArgumentException("Retained evidence is missing.", nameof(resolver));
        MachineQueryPlanIdentity.Validate(refs.QueryPlanRef, value.QueryPlan); MachineQueryRenderReceiptIdentity.Validate(refs.RenderReceiptRef, value.RenderReceipt); HttpObservationIdentity.Validate(refs.ObservationRef, value.Observation); MachineRequestEvidenceIdentity.Validate(refs.RequestEvidenceRef, value.RequestEvidence);
        MachineRequestEvidenceBundle.ValidateRetained(value.QueryPlan, refs.QueryPlanRef, value.RenderReceipt, refs.RenderReceiptRef, value.Observation, value.RequestEvidence); MachineQueryBinder.VerifyOffline(value.QueryPlan, refs.QueryPlanRef, value.QueryInput, value.RenderReceipt, value.Renderer);
        var payload = value.RetainedPayloadBytes.ToArray();
        var isCount = family == profile.CountQueryFamilyRef;
        if (value.QueryPlan.QueryFamilyRef != family || value.QueryInput.QueryFamilyRef != family || value.QueryInput.ArtifactRef != refs.QueryInputRef || value.QueryPlan.OrderedParameterSet != refs.QueryInputRef || isCount && value.QueryPlan.ResponseCardinality.Kind != MachineResponseCardinalityKind.OpaqueBody || !isCount && value.QueryPlan.ResponseCardinality.Kind != MachineResponseCardinalityKind.BoundedRowSetPage || value.Observation.StatusCode != 200 || value.Observation.StatusDisposition != HttpStatusDisposition.DerivableStatus || value.Observation.DurableBlobRef.ByteLength != payload.Length || value.Observation.DurableBlobRef.ContentSha256 != Sha(payload) || value.Observation.ResponseMetadata.ContentType is not SingleHttpHeader contentType || contentType.Value != profile.ExpectedMediaType) throw new ArgumentException("The retained SPARQL evidence tuple does not bind.", nameof(refs));
        RequireInputRoleShape(value.QueryInput, profile, isCount);
        return value with { RetainedPayloadBytes = payload };
    }
    private static IReadOnlyList<RepeatedEnumerationResolvedEvidence> ResolvePages(EnumerationPageSetRefs pageSet, RepeatedEnumerationInterpretationProfile profile, IRepeatedEnumerationEvidenceResolver resolver)
    {
        var pages = pageSet.Pages?.ToArray() ?? throw new ArgumentNullException(nameof(pageSet.Pages));
        if (pages.Length is < 1 or > 1_000_000 || pages.Select((page, index) => page is null || page.Ordinal != index).Any(static invalid => invalid)) throw new ArgumentException("Pages require bounded contiguous ordinals.", nameof(pageSet));
        return pages.Select(page => Resolve(page.Evidence, profile, profile.PageQueryFamilyRef, resolver)).ToArray();
    }
    private static IReadOnlyList<RepeatedEnumerationRow> VerifyPages(IReadOnlyList<RepeatedEnumerationResolvedEvidence> pages, SourceArtifactRef countRef, long count, RepeatedEnumerationInterpretationProfile profile)
    {
        var all = new List<RepeatedEnumerationRow>(); long? limit = null; IReadOnlyList<RepeatedEnumerationRow>? prior = null;
        foreach (var page in pages)
        {
            var card = page.QueryPlan.ResponseCardinality; if (card.Kind != MachineResponseCardinalityKind.BoundedRowSetPage || card.ExpectedPartitionRowCount != count || card.ExpectedPartitionRowCountEvidenceRef != countRef || limit is not null && card.RowLimit != limit) throw new ArgumentException("The page cardinality does not bind the preceding count."); limit ??= card.RowLimit;
            var rows = ParseRows(page.RetainedPayloadBytes.Span, profile); if (rows.Count > limit) throw new ArgumentException("The page exceeds its row limit.");
            var hasCursor = IntegerParameter(page.QueryInput, profile.HasCursorParameterName);
            if (prior is null)
            {
                if (hasCursor != 0) throw new ArgumentException("The first page must not claim a continuation cursor.");
            }
            else
            {
                var cursors = Parameters(page.QueryInput, profile.CursorParameterNames);
                var expected = prior[^1].Cursor.Select(static term => term.Value ?? throw new ArgumentException("A cursor term must be bound.")).ToArray();
                RequireContinuation(
                    hasCursor,
                    cursors,
                    expected,
                    prior.Count,
                    limit ?? throw new InvalidOperationException("The page limit was not established."));
            }
            if (rows.SelectMany(static row => row.Cursor).Any(static term => term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype is not null || term.Language is not null)) throw new ArgumentException("Cursor projections must be plain literals matching the query comparator.");
            if (rows.SelectMany(static row => row.CanonicalKey).Any(static term => term.Kind is RepeatedEnumerationRdfTermKind.Unbound or RepeatedEnumerationRdfTermKind.BlankNode)) throw new ArgumentException("Canonical-key components must be bound and stable.");
            all.AddRange(rows); prior = rows;
        }
        if (prior!.Count >= limit) throw new ArgumentException("The final page must be short or empty.");
        var keys = new HashSet<string>(); for (var i = 0; i < all.Count; i++) if (!keys.Add(Digest("repeated_enumeration_key/1", [all[i].CanonicalKey])) || i > 0 && Compare(all[i - 1].Cursor, all[i].Cursor) >= 0) throw new ArgumentException("Keys must be unique and cursors strictly increase.");
        return all;
    }
    private static long ParseCount(ReadOnlySpan<byte> bytes, RepeatedEnumerationInterpretationProfile profile)
    {
        var rows = Parse(bytes, [profile.CountVariable], profile.Dialect); if (rows.Count != 1 || CountWireType(bytes) != (profile.Dialect == RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso ? "typed-literal" : "literal")) throw new ArgumentException("The count response does not match its explicit Virtuoso dialect."); var term = rows[0][0]; if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Datatype != "http://www.w3.org/2001/XMLSchema#integer" || term.Language is not null || !long.TryParse(term.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 0) throw new ArgumentException("The count binding is not one typed nonnegative integer literal."); return count;
    }
    private static IReadOnlyList<RepeatedEnumerationRow> ParseRows(ReadOnlySpan<byte> bytes, RepeatedEnumerationInterpretationProfile profile)
    {
        var parsed = Parse(bytes, profile.ProjectionVariables, profile.Dialect); return parsed.Select(terms => new RepeatedEnumerationRow(Array.AsReadOnly(terms), Pick(terms, profile.ProjectionVariables, profile.CanonicalKeyVariables), Pick(terms, profile.ProjectionVariables, profile.CursorVariables))).ToArray();
    }
    private static List<RepeatedEnumerationRdfTerm[]> Parse(ReadOnlySpan<byte> bytes, IReadOnlyList<string> expected, RepeatedEnumerationSparqlJsonDialect dialect)
    {
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var root = document.RootElement;
        Object(root, "root", ["head", "results"]);
        var head = root.GetProperty("head");
        Object(head, "head", ["link", "vars"]);
        if (head.GetProperty("link").ValueKind != JsonValueKind.Array ||
            head.GetProperty("link").GetArrayLength() != 0)
        {
            throw new ArgumentException("Virtuoso head.link must be empty.");
        }

        var variables = head.GetProperty("vars");
        if (variables.ValueKind != JsonValueKind.Array ||
            !variables.EnumerateArray()
                .Select(static item => item.GetString()!)
                .SequenceEqual(expected))
        {
            throw new ArgumentException("The SPARQL projection drifted.");
        }

        var results = root.GetProperty("results");
        Object(results, "results", ["distinct", "ordered", "bindings"]);
        if (results.GetProperty("distinct").ValueKind != JsonValueKind.False ||
            results.GetProperty("ordered").ValueKind != JsonValueKind.True)
        {
            throw new ArgumentException("Virtuoso result flags differ.");
        }

        var bindings = results.GetProperty("bindings");
        if (bindings.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("SPARQL bindings must be an array.");
        }

        return bindings.EnumerateArray().Select(binding =>
        {
            Object(
                binding,
                "binding",
                binding.EnumerateObject().Select(static property => property.Name).ToArray());
            if (binding.EnumerateObject().Any(
                    property => !expected.Contains(property.Name, StringComparer.Ordinal)))
            {
                throw new ArgumentException("A binding contains an unknown variable.");
            }

            return expected.Select(variable =>
                    binding.TryGetProperty(variable, out var term)
                        ? Term(term, dialect)
                        : RepeatedEnumerationRdfTerm.Unbound())
                .ToArray();
        }).ToList();
    }
    private static RepeatedEnumerationRdfTerm Term(JsonElement element, RepeatedEnumerationSparqlJsonDialect dialect)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException("A SPARQL term must be an object.");
        var names = element.EnumerateObject().Select(static property => property.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || !element.TryGetProperty("type", out var type) || !element.TryGetProperty("value", out var value) || type.ValueKind != JsonValueKind.String || value.ValueKind != JsonValueKind.String) throw new ArgumentException("The SPARQL term shape is invalid.");
        var lexical = value.GetString()!;
        return type.GetString() switch
        {
            "uri" when Exact(names, "type", "value") => RepeatedEnumerationRdfTerm.Iri(lexical),
            "bnode" when Exact(names, "type", "value") => RepeatedEnumerationRdfTerm.BlankNode(lexical),
            "typed-literal" when dialect == RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso && Exact(names, "type", "value", "datatype") => RepeatedEnumerationRdfTerm.Literal(lexical, NonemptyString(element, "datatype"), null),
            "literal" when Exact(names, "type", "value") => RepeatedEnumerationRdfTerm.Literal(lexical, null, null),
            "literal" when Exact(names, "type", "value", "datatype") => RepeatedEnumerationRdfTerm.Literal(lexical, NonemptyString(element, "datatype"), null),
            "literal" when Exact(names, "type", "value", "xml:lang") => RepeatedEnumerationRdfTerm.Literal(lexical, null, NonemptyString(element, "xml:lang")),
            _ => throw new ArgumentException("The SPARQL term members or kind are unsupported by this dialect."),
        };
    }
    private static bool Exact(IReadOnlyList<string> actual, params string[] expected) => actual.Count == expected.Length && !actual.Except(expected, StringComparer.Ordinal).Any();
    private static string NonemptyString(JsonElement element, string name) { var value = element.GetProperty(name); if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString())) throw new ArgumentException("A SPARQL qualifier must be a nonempty string.", name); return value.GetString()!; }
    private static string CountWireType(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        return document.RootElement.GetProperty("results").GetProperty("bindings")[0].EnumerateObject().Single().Value.GetProperty("type").GetString()!;
    }
    private static void Object(JsonElement element, string name, IReadOnlyList<string> allowed) { if (element.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{name} must be an object."); var names = element.EnumerateObject().Select(static property => property.Name).ToArray(); if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(property => !allowed.Contains(property, StringComparer.Ordinal)) || allowed.Any(required => !names.Contains(required, StringComparer.Ordinal))) throw new ArgumentException($"{name} has duplicate, missing or unknown members."); }
    private static IReadOnlyList<RepeatedEnumerationRdfTerm> Pick(RepeatedEnumerationRdfTerm[] terms, IReadOnlyList<string> projection, IReadOnlyList<string> selected) => Array.AsReadOnly(selected.Select(variable => terms[projection.IndexOf(variable)]).ToArray());
    private static void RequireSameSelection(IEnumerable<RepeatedEnumerationResolvedEvidence> values, RepeatedEnumerationInterpretationProfile profile) { var selected = values.Select(value => SelectionParameters(value.QueryInput, profile.SelectionParameterNames)).ToArray(); if (selected.Skip(1).Any(value => !value.SequenceEqual(selected[0]))) throw new ArgumentException("Selection parameters differ."); }
    private static MachineQueryParameter[] SelectionParameters(MachineQueryInputArtifact input, IReadOnlyList<string> names) => names.Select(name => { var matches = input.OrderedParameters.Where(parameter => parameter.Name == name).ToArray(); if (matches.Length != 1) throw new ArgumentException("A required selection parameter is missing."); return matches[0]; }).ToArray();
    private static long IntegerParameter(MachineQueryInputArtifact input, string name) { var matches = input.OrderedParameters.Where(parameter => parameter.Name == name).ToArray(); if (matches.Length != 1 || matches[0].Kind != MachineQueryParameterKind.BoundedInteger || matches[0].IntegerValue is null) throw new ArgumentException("A required bounded page-state parameter is missing."); return matches[0].IntegerValue.GetValueOrDefault(); }
    private static void RequireInputRoleShape(MachineQueryInputArtifact input, RepeatedEnumerationInterpretationProfile profile, bool isCount)
    {
        var expected = profile.SelectionParameterNames.Append(profile.PassParameterName).ToList();
        if (!isCount)
        {
            expected.Add(profile.HasCursorParameterName);
            if (IntegerParameter(input, profile.HasCursorParameterName) == 1) expected.AddRange(profile.CursorParameterNames);
        }
        var actual = input.OrderedParameters.Select(static parameter => parameter.Name).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) throw new ArgumentException("The ordered machine input parameter roles are not exact.");
    }
    private static string[] Parameters(MachineQueryInputArtifact input, IReadOnlyList<string> names) => names.Select(name => { var matches = input.OrderedParameters.Where(parameter => parameter.Name == name).ToArray(); if (matches.Length != 1 || matches[0].Kind != MachineQueryParameterKind.PublisherCursor || matches[0].TextValue is not { } encoded) throw new ArgumentException("A required typed parameter is missing."); return EnumerationCursorEnvelope.Decode(encoded); }).ToArray();
    private static int Compare(IReadOnlyList<RepeatedEnumerationRdfTerm> left, IReadOnlyList<RepeatedEnumerationRdfTerm> right) { for (var i = 0; i < Math.Min(left.Count, right.Count); i++) { var comparison = EnumerationCursorEnvelope.CompareRaw(left[i].Value!, right[i].Value!); if (comparison != 0) return comparison; } return left.Count.CompareTo(right.Count); }
    private static string Digest(string schema, IEnumerable<IReadOnlyList<RepeatedEnumerationRdfTerm>> tuples) { var document = new CanonicalTupleDocument(schema, tuples.Select(static tuple => tuple.ToArray()).ToArray()); return Sha(ContractCanonicalizer.Canonicalize(document, schema + "-canonical-json", 64)); }
    private static EnumerationPageSetRefs Snapshot(EnumerationPageSetRefs pageSet) => new(Array.AsReadOnly(pageSet.Pages.Select(page => new RepeatedEnumerationPageRef(page.Ordinal, page.Evidence)).ToArray()));
    private static void RequireDistinct(RepeatedEnumerationEvidenceRefs countA, EnumerationPageSetRefs pagesA, RepeatedEnumerationEvidenceRefs countB, EnumerationPageSetRefs pagesB) { var all = new[] { countA }.Concat(pagesA.Pages.Select(static page => page.Evidence)).Append(countB).Concat(pagesB.Pages.Select(static page => page.Evidence)).ToArray(); if (new Func<RepeatedEnumerationEvidenceRefs, SourceArtifactRef>[] { static value => value.QueryInputRef, static value => value.RenderReceiptRef, static value => value.RequestEvidenceRef, static value => value.ObservationRef }.Any(selector => all.Select(selector).Distinct().Count() != all.Length)) throw new ArgumentException("The retained request and observation identities must be distinct."); }
    private static void RequireDistinctPasses(IReadOnlyList<RepeatedEnumerationResolvedEvidence> evidenceA, IReadOnlyList<RepeatedEnumerationResolvedEvidence> evidenceB, RepeatedEnumerationInterpretationProfile profile)
    {
        var passA = evidenceA.Select(value => IntegerParameter(value.QueryInput, profile.PassParameterName)).Distinct().ToArray();
        var passB = evidenceB.Select(value => IntegerParameter(value.QueryInput, profile.PassParameterName)).Distinct().ToArray();
        if (passA.Length != 1 || passB.Length != 1 || passA[0] == passB[0]) throw new ArgumentException("The two evidence sets must use distinct internally consistent pass values.");
    }
    private static void RequireSamePartition(IEnumerable<RepeatedEnumerationResolvedEvidence> evidence)
    {
        if (evidence.Select(static value => value.QueryInput.PartitionBinding.MemberKey).Distinct(StringComparer.Ordinal).Count() != 1) throw new ArgumentException("The retained evidence sets must bind the same partition.");
    }
    private static void RequireDifferentPageLimits(IReadOnlyList<RepeatedEnumerationResolvedEvidence> evidenceA, IReadOnlyList<RepeatedEnumerationResolvedEvidence> evidenceB)
    {
        var limitA = evidenceA[0].QueryPlan.ResponseCardinality.RowLimit;
        var limitB = evidenceB[0].QueryPlan.ResponseCardinality.RowLimit;
        if (limitA is null || limitB is null || limitA == limitB) throw new ArgumentException("The two enumeration passes must use different page limits.");
    }
    private static string Sha(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private sealed record CanonicalTupleDocument(string Schema, IReadOnlyList<IReadOnlyList<RepeatedEnumerationRdfTerm>> Tuples);
}

internal static class RepeatedEnumerationListExtensions { public static int IndexOf<T>(this IReadOnlyList<T> source, T value) { for (var i = 0; i < source.Count; i++) if (EqualityComparer<T>.Default.Equals(source[i], value)) return i; return -1; } }
