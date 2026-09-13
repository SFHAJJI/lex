using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>One bound count or page request of the per-act family, assembled only by the plan.</summary>
/// <remarks>
/// THE CONSTRUCTOR IS INTERNAL, AND THAT DIVERGES FROM EVERY SIBLING BOUND-QUERY RECORD ON PURPOSE.
/// The siblings are plain public positional records, which is fine for them: their partition key
/// is a constant. Here the key IS the claim - it names which act was asked about - so a record that
/// could be assembled from one act's input artifact beside another act's request would carry a key
/// that lies about the traffic it sits next to. Preflight review constructed exactly that from the
/// public constructor. Now only <see cref="LuxembourgConsolidationByActDiscoveryPlan"/>'s own
/// <c>Bind</c> assembles one, from one act, in one call.
/// </remarks>
public sealed record LuxembourgConsolidationByActBoundQuery
{
    internal LuxembourgConsolidationByActBoundQuery(
        MachineQueryPlan machinePlan,
        SourceArtifactRef machinePlanRef,
        MachineQueryInputArtifact inputArtifact,
        BoundMachineRequest request)
    {
        MachinePlan = machinePlan ?? throw new ArgumentNullException(nameof(machinePlan));
        MachinePlanRef = machinePlanRef ?? throw new ArgumentNullException(nameof(machinePlanRef));
        InputArtifact = inputArtifact ?? throw new ArgumentNullException(nameof(inputArtifact));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public MachineQueryPlan MachinePlan { get; }
    public SourceArtifactRef MachinePlanRef { get; }
    public MachineQueryInputArtifact InputArtifact { get; }
    public BoundMachineRequest Request { get; }
}

/// <summary>
/// Asks Legilux which coordinated texts consolidate ONE named act, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// WHY A FAMILY PER ACT RATHER THAN ONE FAMILY OVER ALL ACTS. #419's whole subject is the claim
/// "this act was never consolidated", and #584's review established that the build could not make
/// that claim honestly: <see cref="Absence.AbsenceFamilyEnumerationProof"/> publishes a family key,
/// two profile references, an acquisition run, a row count, a key digest and a retention class - and
/// NO PARTITION BOUNDS. A zero-row proof of an unrelated family satisfied every check an entry could
/// perform, so a frame could pair "no rows" with an act the enumeration had never asked about.
/// </para>
/// <para>
/// The fix accepted by design review on 2026-09-13 is to make the family key itself carry the act:
/// <c>AbsenceFamilyEnumerationProof.TryCreate</c> already requires
/// <c>delivery.PartitionKey == familyKey</c>, and the comparison obtains that partition from the
/// canonical machine-input artifacts of both counts and every page. So a per-act partition key ties
/// the enumeration's scope to an act identity structurally, with NO change to the shared absence or
/// core machinery.
/// </para>
/// <para>
/// THE DIGEST-SHAPED KEY ALONE PROVES NOTHING ABOUT QUERY SEMANTICS, and that limit is the reason
/// for most of this file. A key is a name, and this repository's recurring defect is a name claiming
/// more than its evidence supports. Four things carry the meaning instead, and each is tested by
/// deleting it:
/// <list type="number">
/// <item>one function derives the key from the exact validated act IRI, and nothing else spells it;</item>
/// <item>the same act IRI is a REQUIRED typed selection parameter in the canonical input; the
/// plan's own door derives the key from it, and the RENDERER - the one choke point every send
/// passes through - recomputes the key from the bound parameter and refuses to render an input
/// whose partition names a different act. Preflight review built such an input through the
/// public <c>MachineQueryInputArtifact.Create</c>, which validates a partition key and a
/// parameter list independently; the renderer check is what closes that path, and it is the
/// design review's condition 2 stated as code rather than as a property of one constructor;</item>
/// <item>both templates restrict the consolidation relation to that bound parameter, so a family
/// name is never asked to supply the restriction;</item>
/// <item>the interpretation profile binds the exact families, projection, canonical keys, cursors
/// and selection-parameter name.</item>
/// </list>
/// </para>
/// <para>
/// THE FULL DIGEST, NOT A PREFIX OF IT. <see cref="EuObjectFactsDiscoveryPlan.PartitionKeyFor"/> is
/// the closest precedent and it truncates to twenty-four hex characters, which is right for a batch
/// key whose members are also carried in the input. It is wrong here: this key is the only thing
/// standing between "proven for this act" and "proven for some act", so it carries the whole
/// lowercase SHA-256. The result is 27 + 64 = 91 characters, inside the 128-character machine-member
/// limit.
/// </para>
/// <para>
/// THE RELATION AND ITS DIRECTION ARE READ OUT OF THIS BUILD'S OWN REVIEWED VOCABULARY, not chosen
/// here. <c>LuxembourgSourceProfile</c> admits <c>jolux:consolidates</c> with
/// <c>LuxembourgRelationSemantic.ConsolidatesShapeRequired</c>;
/// <see cref="LuxembourgConsolidatesDirection.AssertedSubjectToObject"/> fixes the direction and
/// <see cref="LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct"/> names the accepted
/// shape. So a coordinated text is the SUBJECT and the act it consolidates is the OBJECT, and the
/// consolidations of an act are found by asking what points AT it.
/// </para>
/// <para>
/// NO CLASS IS REQUIRED OF THE SUBJECT, AND THE FIRST HEAD OF THIS FILE GOT THAT WRONG IN THE ONE
/// DIRECTION THAT MATTERS. It required <c>?consolidation a jolux:Consolidation</c>, on the reading
/// that a coordinated text is classed as a Consolidation. The resolver's own accepted shape says
/// the opposite: <c>LuxembourgConsolidatesShape</c> admits
/// <see cref="LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct"/> only when the subject's
/// class set is EXACTLY <c>{jolux:Act}</c>, with type-document TC. A real, accepted consolidation
/// would therefore have matched the relation and failed the class pattern, and the family would have
/// returned zero rows for a consolidated act - a false "never consolidated", which is the single
/// defect this whole programme exists to close. A preflight lens found it by reading the resolver;
/// the restriction is gone, and <c>ANoClassIsRequiredOfTheSubject</c> pins its absence against the
/// resolver's invariant so the two cannot drift apart again. Classifying what came back is the
/// decoder's job, downstream, where a wrong class becomes a typed row disposition rather than a
/// silent absence.
/// </para>
/// <para>
/// THE ACT IS A PROJECTED COLUMN, NOT ONLY A BOUND CONSTANT. Design review condition 4 requires the
/// decoder to check that EVERY delivered row names the selected act before assigning it meaning,
/// and explicitly forbids consuming only the row count or key digest. A row can only be checked
/// against a column it carries, so the act is bound through <c>VALUES ?act { ... }</c> and
/// <c>?act</c> is projected. Each returned row's <c>act</c> is then the value the publisher's own
/// triple joined against, not an echo of the request - and the decoder can require it row by row.
/// </para>
/// <para>
/// WHAT A ZERO-ROW DELIVERY FROM THIS FAMILY MEANS, STATED EXACTLY. It means the publisher returned
/// no subject asserting <c>jolux:consolidates</c> against this act, in this run, under this
/// profile, with no class or type restriction narrowing "subject". It does not mean the act is
/// unconsolidated in law, and nothing here may be read as the terminal population count - that
/// remains unbuilt, and the bounded live acceptance that would let this family be believed against
/// the real publisher is a separate disposition that has not been taken.
/// </para>
/// <para>
/// OFFLINE. This plan renders requests; it sends none. No ceiling is dispositioned here and no
/// traffic is authorized by its existence.
/// </para>
/// </remarks>
public sealed class LuxembourgConsolidationByActDiscoveryPlan
{
    /// <summary>
    /// The versioned prefix every per-act family key carries.
    /// </summary>
    /// <remarks>
    /// VERSIONED BECAUSE THE KEY IS A CLAIM ABOUT WHAT WAS ASKED. If the templates, the projection
    /// or the relation ever change, a key minted under the old rules would otherwise be
    /// indistinguishable from one minted under the new, and two enumerations asking different
    /// questions would share a partition. The version moves with the question.
    /// </remarks>
    public const string PartitionKeyPrefix = "lu-consolidation-by-act-v1-";

    /// <summary>The name of the typed selection parameter carrying the act this family is about.</summary>
    public const string ActSelectionParameterName = "act_iri";

    /// <summary>
    /// The JOLux ontology prefix, aliased from the draft-graph plan rather than respelled.
    /// </summary>
    /// <remarks>
    /// Taken from the verified source profile rather than respelled here. A family that spelled its
    /// own prefix would render a template no decoder reads while every test derived from the same
    /// private spelling and agreed - the shared-convention hazard the opinion-request inventory
    /// records for its own markers. The profile is also the thing that ADMITS the relation below, so
    /// taking the prefix from anywhere else would let the two drift.
    /// </remarks>
    private const string Jolux = VerifiedLuxembourgSourceProfile.JoluxPrefix;

    /// <summary>The relation a coordinated text asserts against the act it consolidates.</summary>
    public const string ConsolidatesPredicateIri = Jolux + "consolidates";

    /// <summary>The marker a row carries when its subject is a publisher IRI.</summary>
    /// <remarks>Aliased rather than restated, per the Luxembourg families' shared convention.</remarks>
    public const string IriKind = LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind;

    /// <summary>The marker a row carries when its subject is a blank node.</summary>
    public const string UnsupportedBlankNodeKind =
        LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind;

    internal const long PublisherDeliveryCeilingRows = 1_000_000;

    internal const uint Pass1PageLimit = 449;
    internal const uint Pass2PageLimit = 283;

    private const string ResourceId = "urn:uuid:8c41d7b9-5a02-4e36-b1f7-0d29e5a36c14";
    private const string MemberPrefix = "lu-consolidation-by-act";
    private const string ResponseMediaType = "application/sparql-results+json";
    private const string ThresholdDetectorIdentity = "enumeration-row-threshold/1";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] Projection =
        ["consolidation", "consolidation_kind", "act", "multiplicity", "key_1", "key_2"];

    /// <summary>
    /// The keyset: the coordinated text, and its kind.
    /// </summary>
    /// <remarks>
    /// The kind carries AUTHORITY rather than uniqueness, exactly as the opinion-request inventory's
    /// does: a blank node's label and an IRI can share a lexical form, and two rows that are
    /// different facts must not share every key.
    /// </remarks>
    private static readonly string[] Cursor = ["key_1", "key_2"];

    internal static int CursorKeyCount => Cursor.Length;

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgConsolidationByActDiscoveryPlan()
    {
        (CountTemplate, PageTemplate) = BuildTemplates();
        _canonicalIdentityBytes = StrictUtf8.GetBytes(string.Join('\n', new[]
        {
            "lu-consolidation-by-act-plan/1",
            "endpoint=" + LuxembourgQueryPlan.PublisherEndpoint,
            "method=POST",
            "request_media_type=application/x-www-form-urlencoded",
            "response_media_type=" + ResponseMediaType,
            "consolidates=" + ConsolidatesPredicateIri,
            "subject_class_restriction=none",
            "iri_kind=" + IriKind,
            "unsupported_blank_node_kind=" + UnsupportedBlankNodeKind,
            "partition_key_prefix=" + PartitionKeyPrefix,
            "publisher_delivery_ceiling_rows=" + PublisherDeliveryCeilingRows.ToString(CultureInfo.InvariantCulture),
            "pass_1=" + (int)LuxembourgQueryPass.Pass1 + ":" + Pass1PageLimit,
            "pass_2=" + (int)LuxembourgQueryPass.Pass2 + ":" + Pass2PageLimit,
            "terminal_page_policy=short_page_terminal",
            "projection=" + string.Join(',', Projection),
            "canonical_keys=" + string.Join(',', Cursor),
            "cursor=" + string.Join(',', Cursor),
            "selection_parameters=" + ActSelectionParameterName,
            "count_member=" + MemberPrefix + ".count",
            "page_member=" + MemberPrefix + ".page",
            CountTemplate,
            PageTemplate,
        }));
        ArtifactRef = new SourceArtifactRef(ResourceId, Sha256(_canonicalIdentityBytes));
        CountQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".count");
        PageQueryFamilyRef = new SourceRegistryMemberRef(ArtifactRef, MemberPrefix + ".page");
    }

    public string PublisherEndpoint => LuxembourgQueryPlan.PublisherEndpoint;
    public SourceArtifactRef ArtifactRef { get; }
    public SourceRegistryMemberRef CountQueryFamilyRef { get; }
    public SourceRegistryMemberRef PageQueryFamilyRef { get; }
    public string CountTemplate { get; }
    public string PageTemplate { get; }

    public static LuxembourgConsolidationByActDiscoveryPlan Create() => new();

    internal byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    /// <summary>
    /// The one function that derives a per-act family key. Nothing else spells one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE SPELLING, AND NO NORMALIZATION ANYWHERE ELSE. The digest is taken over the bytes of the
    /// act IRI exactly as <see cref="CanonicalizeSelection"/> validated it, so a caller cannot reach
    /// a different key by presenting a different-but-equivalent spelling: an unadmitted spelling
    /// does not get a key at all, it throws. Every other member of this build that needs the key for
    /// an act must call this, and a second derivation written elsewhere would be a second opinion
    /// about what the act is.
    /// </para>
    /// <para>
    /// The prefix is outside the digest on purpose. It is a human-readable statement of which
    /// question was asked, and keeping it outside means a reader can see the version without
    /// recomputing anything, while the part that identifies the act stays a whole digest.
    /// </para>
    /// </remarks>
    /// <param name="publisherActIri">The act this family is about, as the publisher spells it.</param>
    /// <exception cref="ArgumentException">
    /// The IRI is not an exact Legilux act ELI. A caller contract violation, not a data disagreement:
    /// there is no honest key for an act this build cannot name.
    /// </exception>
    public static string PartitionKeyFor(string publisherActIri) =>
        PartitionKeyPrefix + Sha256(StrictUtf8.GetBytes(CanonicalizeSelection(publisherActIri)));

    /// <summary>
    /// The exact act IRI this family binds, validated. The only admitted spelling.
    /// </summary>
    /// <remarks>
    /// Returned rather than merely checked, so the caller that renders and the caller that keys
    /// cannot drift: <see cref="PartitionKeyFor"/> digests this function's OUTPUT, and
    /// <see cref="BindCount"/> and <see cref="BindPage"/> bind that same output as the selection
    /// parameter.
    /// </remarks>
    public static string CanonicalizeSelection(string publisherActIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherActIri);

        // EXACTLY ONE RAW SPELLING, CHECKED ON THE BYTES AND NOT ON A PARSED URI. The first head of
        // this method refused only the relative "eli/..." form and let OfficialIdentifier.EliMintedBy
        // plus System.Uri decide the rest. Preflight review found three more spellings of one act
        // that passed and minted three different keys: the alias host legilux.public.lu (EliMintedBy
        // maps both hosts to the same publisher), an upper-case host (System.Uri lowercases .Host
        // before the lookup, then the raw mixed-case string was digested), and https for http (both
        // schemes admitted). Each one silently split an act's evidence across two families.
        //
        // Normalizing them together is the obvious alternative and it is the one the design review
        // forbids - "no normalization or alternate spelling introduced elsewhere" - because a
        // normalizer is an alternate-spelling rule that then has to track whatever the publisher
        // does next. So the raw string must begin with the one exact prefix Legilux mints act ELIs
        // under, byte for byte, and everything else is refused. This is the same discipline
        // FactsCommon applies to Cellar authorities for the same reason: System.Uri's own
        // case-folding is what makes a parsed check the wrong tool for a spelling decision.
        //
        // AND ONE PARSED SPELLING TOO, WHICH THE RAW PREFIX ALONE DID NOT GIVE. Codex's review of
        // the second head reproduced what that check still admitted: `loi\2017/...` beside
        // `loi/2017/...`. System.Uri reads both as one absolute URI - it rewrites the backslash -
        // and this method returned the raw bytes of each, so one act minted two keys: the
        // split-family defect this method exists to close, reintroduced one directory deeper. So
        // the offered text must now equal, byte for byte, what the parser would print back for it.
        // A backslash, a dot segment, an explicit default port or anything else the parser would
        // rewrite is a second spelling and is refused. This is still not normalization: nothing is
        // rewritten and returned; the caller's bytes either are the parsed form or they throw.
        //
        // AND QUERY-SAFE, BEFORE ANY REQUEST EXISTS. The value ends up inside <...> in a SPARQL
        // request, and IRIREF (SPARQL 1.1 grammar rule 139) excludes <, >, ", {, }, |, ^, the
        // backtick, the backslash and every control character up to space. The same review
        // reproduced a bound request carrying `<...leg/x"y>`: the renderer's own term check
        // refused only angle brackets and whitespace. The admitted alphabet is therefore printable
        // ASCII minus the IRIREF exclusions - the discipline FactsCommon.IsExactUriSpelling applies
        // to Cellar identifiers - and minus three more: ? and # (a Legilux act ELI carries no query
        // or fragment, and either would let one act spell itself with a suffix) and % (an escaped
        // byte is a second spelling of an unescaped one, and the publisher mints these from plain
        // ASCII). Non-ASCII is outside the alphabet for the same reason: the parser would escape it.
        //
        // THREE CHECKS IN ORDER, EACH REFUSING IN ITS OWN WORDS. The order matters twice: the
        // alphabet runs before the parser sees the text, so nothing here depends on how System.Uri
        // copes with a control character; and each refusal names the rule that fired, so a test can
        // pin WHICH rule refused a spelling. That is what makes every clause here establishable by
        // deletion - most IRIREF exclusions would also fail the parsed-form comparison (the parser
        // escapes them), and one shared message would let the alphabet rule be deleted unnoticed.
        //
        // OfficialIdentifier.EliMintedBy IS NO LONGER CONSULTED HERE, and that is deliberate. The
        // prefix fixes the scheme, the host and the /eli/ path it tests; the alphabet excludes the
        // space it refuses. Nothing it would reject can reach the end of this method, so a call
        // would be a guard no deletion could establish. The tests still premise their hostile
        // spellings on that authority admitting them, which is where it is rightly consulted.
        if (!publisherActIri.StartsWith(AdmittedActIriPrefix, StringComparison.Ordinal) ||
            publisherActIri.Length == AdmittedActIriPrefix.Length)
        {
            throw Refused(
                publisherActIri, nameof(publisherActIri),
                $"it does not begin '{AdmittedActIriPrefix}' and continue past it");
        }

        if (!publisherActIri.All(IsAdmittedActIriCharacter))
        {
            throw Refused(
                publisherActIri, nameof(publisherActIri),
                "it carries a character outside the admitted alphabet, which is printable ASCII "
                + "minus SPARQL IRIREF's exclusions and minus '?', '#' and '%'");
        }

        if (!Uri.TryCreate(publisherActIri, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.AbsoluteUri, publisherActIri, StringComparison.Ordinal))
        {
            throw Refused(
                publisherActIri, nameof(publisherActIri),
                "it is not equal to its own parsed absolute form");
        }

        return publisherActIri;
    }

    private static ArgumentException Refused(string offered, string parameterName, string because) =>
        new($"'{offered}' is not a Legilux act ELI in the one admitted spelling: {because}.", parameterName);

    /// <summary>
    /// Whether a character may appear in an admitted act IRI: printable ASCII, minus every
    /// character SPARQL's IRIREF excludes, minus the three that would let one act spell itself two
    /// ways.
    /// </summary>
    private static bool IsAdmittedActIriCharacter(char value) =>
        value is > ' ' and <= '~'
            and not ('<' or '>' or '"' or '{' or '}' or '|' or '^' or '`' or '\\')
            and not ('?' or '#' or '%');

    /// <summary>
    /// The exact raw prefix every act IRI this family admits begins with, byte for byte.
    /// </summary>
    /// <remarks>
    /// Scheme, host and the state-legislation branch together. <c>/eli/etat/leg/</c> carries the
    /// LOI and RGD classes this programme is about, and is narrower than <c>EliMintedBy</c>'s own
    /// <c>/eli/</c> test: that answers "which publisher minted this", this answers "is this an act
    /// this family can be asked about, spelled the one way it keys". Public so the frame and the
    /// decoder can state the same admission rather than a second opinion of it.
    /// </remarks>
    public const string AdmittedActIriPrefix = "http://data.legilux.public.lu/eli/etat/leg/";

    public RepeatedEnumerationInterpretationProfile CreateDeliveryProfile() => new(
        RepeatedEnumerationInterpretationProfile.SchemaId,
        RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso,
        ResponseMediaType,
        EnumerationCursorEnvelope.Identity,
        PublisherDeliveryCeilingRows,
        ThresholdDetectorIdentity,
        CountQueryFamilyRef,
        PageQueryFamilyRef,
        "count",
        Projection,
        Cursor,
        Cursor,
        [ActSelectionParameterName],
        "pass_id",
        Cursor.Select(static value => "last_" + value).ToArray(),
        "has_cursor",
        RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal);

    public LuxembourgConsolidationByActBoundQuery BindCount(
        string publisherActIri,
        LuxembourgQueryPass pass,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(false, publisherActIri, pass, null,
            new MachineResponseCardinality(MachineResponseCardinalityKind.OpaqueBody, null, null, null),
            machinePlanResourceId, inputResourceId, rendererSource);

    public LuxembourgConsolidationByActBoundQuery BindPage(
        string publisherActIri,
        LuxembourgQueryPass pass,
        IReadOnlyList<string>? cursor,
        long expectedPartitionRowCount,
        SourceArtifactRef expectedPartitionRowCountEvidenceRef,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource) =>
        Bind(true, publisherActIri, pass, cursor,
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                PageLimit(pass), expectedPartitionRowCount, expectedPartitionRowCountEvidenceRef),
            machinePlanResourceId, inputResourceId, rendererSource);

    private LuxembourgConsolidationByActBoundQuery Bind(
        bool isPage,
        string publisherActIri,
        LuxembourgQueryPass pass,
        IReadOnlyList<string>? cursor,
        MachineResponseCardinality response,
        string machinePlanResourceId,
        string inputResourceId,
        MachineQueryRendererSource rendererSource)
    {
        _ = PageLimit(pass);
        ArgumentNullException.ThrowIfNull(rendererSource);

        // ONE VALIDATED SPELLING, USED FOR BOTH THINGS IT HAS TO AGREE WITH ITSELF ABOUT. The
        // partition key is DERIVED from this value rather than accepted beside it, so "the key names
        // the act the query asks about" is true by construction and there is no argument list in
        // which a caller could pair this act's key with another act's selection.
        var act = CanonicalizeSelection(publisherActIri);
        var partitionKey = PartitionKeyPrefix + Sha256(StrictUtf8.GetBytes(act));

        // The selection binds BEFORE pass_id: RequireInputRoleShape compares
        // SelectionParameterNames.Append(PassParameterName) by sequence, and the profile above
        // declares exactly this one selection parameter.
        var parameters = new List<MachineQueryParameter>
        {
            new(ActSelectionParameterName, MachineQueryParameterKind.PublisherLiteral, null, act, ArtifactRef),
            new("pass_id", MachineQueryParameterKind.BoundedInteger, (int)pass, null, ArtifactRef),
        };

        if (isPage)
        {
            var values = cursor?.ToArray() ?? [];
            if (values.Length != 0 && values.Length != Cursor.Length)
            {
                throw new ArgumentException(
                    $"A continuation cursor must have {Cursor.Length} exact parts.", nameof(cursor));
            }

            parameters.Add(new MachineQueryParameter(
                "has_cursor", MachineQueryParameterKind.BoundedInteger,
                values.Length == 0 ? 0 : 1, null, ArtifactRef));
            for (var index = 0; index < values.Length; index++)
            {
                parameters.Add(new MachineQueryParameter(
                    "last_" + Cursor[index], MachineQueryParameterKind.PublisherCursor,
                    null, EnumerationCursorEnvelope.Encode(values[index]), ArtifactRef));
            }
        }

        var family = isPage ? PageQueryFamilyRef : CountQueryFamilyRef;
        var input = MachineQueryInputArtifact.Create(
            inputResourceId, family, partitionKey, response, parameters);
        var renderer = new LuxembourgConsolidationByActSparqlRenderer(this, isPage, rendererSource);
        var rendered = renderer.RenderInput(input, response);
        var body = rendered.CopyRequestBody();
        var targetBytes = Encoding.ASCII.GetBytes("/sparqlendpoint");
        var machinePlan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            family,
            ArtifactRef,
            rendererSource.Reference,
            HttpRequestMethod.Post,
            LuxembourgQueryPlan.PublisherEndpoint,
            targetBytes.LongLength,
            Sha256(targetBytes),
            response,
            new SourceRegistryMemberRef(ArtifactRef, "application/x-www-form-urlencoded"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));
        var machinePlanRef = MachineQueryPlanIdentity.Create(machinePlanResourceId, machinePlan);
        return new(machinePlan, machinePlanRef, input,
            MachineQueryBinder.BindForSend(machinePlan, machinePlanRef, input, renderer));
    }

    internal static uint PageLimit(LuxembourgQueryPass pass) => pass switch
    {
        LuxembourgQueryPass.Pass1 => Pass1PageLimit,
        LuxembourgQueryPass.Pass2 => Pass2PageLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    private static (string Count, string Page) BuildTemplates()
    {
        // THE ACT RESTRICTION IS IN BOTH TEMPLATES AND IT IS THE ONLY THING THAT MAKES THEM ABOUT AN
        // ACT. The family key is a name; delete the act from either template and that template
        // enumerates every subject that consolidates anything, while its key still claims one act.
        // Both deletions are mutation-killed independently, because a count restricted to the act
        // beside a page that is not would deliver a page the count never described.
        //
        // The act is bound through VALUES into ?act and PROJECTED rather than written into the
        // triple as a constant, so every returned row carries the act the publisher's own triple
        // joined against and the decoder can check it row by row. And the SUBJECT of the
        // consolidates triple is ?consolidation - the variable that is selected, grouped and keyed.
        // A preflight lens showed that renaming that subject alone cross-joins "something
        // consolidates the act" with "every subject there is" while every test stayed green; the
        // test now asserts the whole triple, subject included.
        //
        // NO CLASS PATTERN ON THE SUBJECT. See the type remarks: the resolver's accepted shape
        // classes a coordinated text as exactly jolux:Act, and a jolux:Consolidation restriction
        // here returned zero rows for a consolidated act.
        var graphPattern = $$"""
              VALUES ?lex_pass_id { {pass_id:uint} }
              VALUES ?act { {act_iri:iri} }
              ?consolidation <{{ConsolidatesPredicateIri}}> ?act .
            """;
        var rows = $$"""
            SELECT ?consolidation ?act (COUNT(*) AS ?multiplicity) WHERE {
            {{Indent(graphPattern)}}
            }
            GROUP BY ?consolidation ?act
            """;
        var count = $$"""
            SELECT (COUNT(*) AS ?count) WHERE {
              {
            {{Indent(Indent(rows))}}
              }
            }
            """;
        var pageRows = $$"""
            SELECT ?consolidation ?consolidation_kind ?act (COUNT(*) AS ?multiplicity) ?key_1 ?key_2 WHERE {
            {{Indent(graphPattern)}}
              BIND(COALESCE(
                IF(isIRI(?consolidation), "{{IriKind}}", "{{UnsupportedBlankNodeKind}}"),
                "{{UnsupportedBlankNodeKind}}") AS ?consolidation_kind)
              BIND(COALESCE(STR(?consolidation), "") AS ?key_1)
              BIND(?consolidation_kind AS ?key_2)
              VALUES (?has_cursor ?last_key_1 ?last_key_2) {
                ({has_cursor:uint} {last_key_1:string} {last_key_2:string})
              }
              FILTER(?has_cursor = 0 || (?key_1 > ?last_key_1)
                || (?key_1 = ?last_key_1 && ?key_2 > ?last_key_2))
            }
            GROUP BY ?consolidation ?consolidation_kind ?act ?key_1 ?key_2
            ORDER BY ?key_1 ?key_2
            LIMIT {page_limit:uint}
            """;
        return (count, pageRows);
    }

    private static string Indent(string value) =>
        string.Join('\n', value.Split('\n').Select(static line => "  " + line));

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal sealed class LuxembourgConsolidationByActSparqlRenderer : IMachineQueryRenderer
{
    private readonly LuxembourgConsolidationByActDiscoveryPlan _plan;
    private readonly bool _isPage;
    private readonly MachineQueryRendererSource _rendererSource;

    internal LuxembourgConsolidationByActSparqlRenderer(
        LuxembourgConsolidationByActDiscoveryPlan plan,
        bool isPage,
        MachineQueryRendererSource rendererSource)
    {
        _plan = plan;
        _isPage = isPage;
        _rendererSource = rendererSource;
    }

    public SourceArtifactRef RendererProfileRef => _plan.ArtifactRef;
    public SourceArtifactRef RendererSourceRef => _rendererSource.Reference;
    public ReadOnlyMemory<byte>? CopyRendererProfileBytes() => _plan.CopyCanonicalIdentityBytes();
    public ReadOnlyMemory<byte>? CopyRendererSourceBytes() => _rendererSource.CopyBytes();

    public MachineQueryRenderOutput Render(MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
        RenderInput(orderedParameterSet, plan.ResponseCardinality);

    internal MachineQueryRenderOutput RenderInput(
        MachineQueryInputArtifact input, MachineResponseCardinality response)
    {
        var parameters = input.OrderedParameters.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        var pass = (LuxembourgQueryPass)Integer(parameters, "pass_id");
        var limit = LuxembourgConsolidationByActDiscoveryPlan.PageLimit(pass);

        // THE ACT IS SUBSTITUTED INTO BOTH TEMPLATES, AND Replace REFUSES A SLOT THAT IS NOT THERE
        // EXACTLY ONCE. That is what makes deleting the restriction from a template a build-visible
        // act rather than a silent widening: a template missing `{act_iri:iri}` throws here on the
        // first render instead of quietly enumerating every coordinated text the publisher holds.
        // A VALID KEY PAIRED WITH ANOTHER ACT MUST NOT RENDER - design review condition 2, enforced
        // at the one place every send passes through rather than only in the plan's own Bind.
        // MachineQueryInputArtifact.Create is public and validates a partition key and a parameter
        // list independently, so an input naming act A in its partition and act B in its selection
        // is constructible; preflight review constructed one. Recomputing the key from the bound
        // parameter here is what makes "the key names the act the query asks about" true of every
        // request that reaches the wire, whatever built the input.
        var act = Literal(parameters, LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName);
        var recomputed = LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor(act);
        if (!string.Equals(input.PartitionBinding.MemberKey, recomputed, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The input's partition key is not the key for the act it selects; a valid key paired "
                + "with another act must not render.",
                nameof(input));
        }

        var query = Replace(_isPage ? _plan.PageTemplate : _plan.CountTemplate,
            "{pass_id:uint}", ((int)pass).ToString(CultureInfo.InvariantCulture));
        query = Replace(
            query,
            "{" + LuxembourgConsolidationByActDiscoveryPlan.ActSelectionParameterName + ":iri}",
            SparqlIriTerm(act));

        if (!_isPage)
        {
            if (response.Kind != MachineResponseCardinalityKind.OpaqueBody || parameters.Count != 2)
            {
                throw new ArgumentException("A count input has one exact shape.", nameof(input));
            }

            return Output(query);
        }

        if (response.Kind != MachineResponseCardinalityKind.BoundedRowSetPage || response.RowLimit != limit)
        {
            throw new ArgumentException("The page limit must come from the pass policy.", nameof(response));
        }

        var hasCursor = Integer(parameters, "has_cursor");
        if (hasCursor is not (0 or 1))
        {
            throw new ArgumentException("Cursor presence must be zero or one.", nameof(input));
        }

        if (parameters.Count != 3 +
            (hasCursor == 1 ? LuxembourgConsolidationByActDiscoveryPlan.CursorKeyCount : 0))
        {
            throw new ArgumentException("A page input has one exact cursor shape.", nameof(input));
        }

        query = Replace(query, "{page_limit:uint}", limit.ToString(CultureInfo.InvariantCulture));
        query = Replace(query, "{has_cursor:uint}", hasCursor.ToString(CultureInfo.InvariantCulture));
        for (var ordinal = 1; ordinal <= LuxembourgConsolidationByActDiscoveryPlan.CursorKeyCount; ordinal++)
        {
            var name = "last_key_" + ordinal;
            var value = hasCursor == 0 ? string.Empty : Cursor(parameters, name);
            query = Replace(query, "{" + name + ":string}", LuxembourgQueryText.SparqlString(value));
        }

        return Output(query);
    }

    private static MachineQueryRenderOutput Output(string query) => new(
        LuxembourgQueryPlan.PublisherEndpoint,
        Encoding.UTF8.GetBytes("query=" + Uri.EscapeDataString(query)));

    private static long Integer(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.BoundedInteger && value.IntegerValue is not null
            ? value.IntegerValue.Value
            : throw new ArgumentException($"The integer input {name} is missing or invalid.");

    private static string Literal(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherLiteral && value.TextValue is not null
            ? value.TextValue
            : throw new ArgumentException($"The literal input {name} is missing or invalid.");

    private static string Cursor(IReadOnlyDictionary<string, MachineQueryParameter> parameters, string name) =>
        parameters.TryGetValue(name, out var value) &&
        value.Kind == MachineQueryParameterKind.PublisherCursor && value.TextValue is not null
            ? EnumerationCursorEnvelope.Decode(value.TextValue)
            : throw new ArgumentException($"The cursor input {name} is missing or invalid.");

    /// <summary>The act as a SPARQL IRI term.</summary>
    /// <remarks>
    /// NO CHARACTER CHECK OF ITS OWN, AND THAT IS A REPAIR. The first head kept one here - angle
    /// brackets and whitespace - as a last line for inputs a caller assembled, and Codex's review
    /// showed it was both weaker than IRIREF and unreachable: <see cref="RenderInput"/> recomputes
    /// the partition key through
    /// <see cref="LuxembourgConsolidationByActDiscoveryPlan.PartitionKeyFor"/> before this runs,
    /// and that call is the one validator, so a value that reaches here has already been admitted
    /// by the full rule. A second, narrower rule here would be a second opinion that no test could
    /// establish by deletion, and the review found the gap between the two opinions.
    /// </remarks>
    private static string SparqlIriTerm(string canonicalIri) => "<" + canonicalIri + ">";

    private static string Replace(string source, string slot, string replacement)
    {
        if (source.Split(slot, StringSplitOptions.None).Length != 2)
        {
            throw new ArgumentException("A renderer slot must occur exactly once.", nameof(source));
        }

        return source.Replace(slot, replacement, StringComparison.Ordinal);
    }
}
