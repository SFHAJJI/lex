using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why <see cref="EuManifestationListingDecode.TryDecode"/> refused to hand back listings. Closed.
/// </summary>
/// <remarks>
/// No member here drops a row silently: every family M row this door reads is either folded into
/// one object's listing or is the exact row that produced one of these refusals.
/// </remarks>
public enum EuManifestationListingRefusal
{
    /// <summary>No refusal: the listings were admitted.</summary>
    None = 0,

    /// <summary>
    /// A family M row's <c>parent</c>, <c>value</c>, <c>value_kind</c>, <c>datatype_iri</c> or
    /// <c>language_tag</c> term disagreed with the shape the family M projection promises. The
    /// office's own listing answers a plain <c>xsd:string</c> literal for
    /// <c>cdm:manifestation_type</c> (observed live on 2026-09-04 for CELEX 32008R0593 and
    /// 31995L0046), so an IRI-valued or language-tagged manifestation type is a publisher shape
    /// change this door refuses rather than reinterprets.
    /// </summary>
    ListingRowTermKindMismatch = 1,

    /// <summary>
    /// A family M row's <c>parent</c> is not a member of this call's own closure. The offending
    /// canonical IRI is reported through <c>offendingIri</c>.
    /// </summary>
    ListingParentNotInClosure = 2,

    /// <summary>
    /// A family M row named a manifestation type outside the closed
    /// <see cref="EuManifestationFormat"/> vocabulary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT a whole-decode refusal, and that is the point. The token is still refused BY NAME, per
    /// SCOPE_RULING lex-event-20260904T173606578Z-9977b89239ed43f98df09972f98a741a precision one
    /// (silently dropping an unknown type would let the ladder claim it had read the office's whole
    /// listing while ignoring part of it), but the refusal is scoped to the ONE WORK whose listing
    /// carried it: that Work's own format observation becomes
    /// <see cref="EuFormatBodyAdmission.BodyNotAdmitted"/> with the token recorded on its reason
    /// code, its body axis becomes a typed gap, and every other Work in the same run is decoded
    /// normally.
    /// </para>
    /// <para>
    /// D1-05d's own REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e
    /// found the original blast radius: one unadmitted token anywhere refused the whole seed's run,
    /// so the day the office lists a new manifestation type on any object, or a case-law xml or AKN
    /// token enters the closure, every EU run would refuse. This is the same per-object containment
    /// the EU document-fetch route already went through: one object's problem must never kill the
    /// run. Whole-decode refusal is now reserved for a row naming a parent outside the closure,
    /// which is a violation of what this call was asked to decode rather than a fact about the
    /// publisher's vocabulary.
    /// </para>
    /// <para>
    /// This member therefore never reaches <c>refusal</c> from
    /// <see cref="EuManifestationListingDecode.TryDecode"/>; it names the condition that
    /// <see cref="EuManifestationListingDecode.UnadmittedTypeReasonCode"/> records on one Work.
    /// </para>
    /// </remarks>
    ManifestationTypeNotInVocabulary = 3,

    /// <summary>
    /// One object carried both a real listed type and the explicit "the office lists nothing"
    /// absence row. The two are mutually exclusive by construction of family M's own UNION, so this
    /// is a delivery disagreement, never a readable listing.
    /// </summary>
    ListingContradictsItsOwnAbsenceRow = 4,
}

/// <summary>
/// D1-05d's producer: turns family M's verified manifestation-listing rows
/// (<see cref="EuObjectFactsQuerySet.ManifestationFacts"/>) into one
/// <see cref="EuFormatObservation"/> per object, carrying the ordered fetch candidates the
/// acquisition step then attempts in order.
/// </summary>
/// <remarks>
/// <para>
/// Authority: SCOPE_RULING lex-event-20260904T173606578Z-9977b89239ed43f98df09972f98a741a and the
/// RULING that superseded part of it,
/// lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262 ("listed is not servable"), which
/// governs wherever the two differ.
/// </para>
/// <para>
/// What the listing is, and what it is not. Family M reads the office's own per-work statement of
/// which manifestation types it offers. That statement is a CANDIDATE SET, never a promise of
/// service: on 2026-09-04, CELEX 32008R0593 listed <c>pdfa1a</c> and answered 404 to
/// <c>Accept: application/pdf;type=pdfa1a</c> with the body "cellar identifier
/// cellar:3db0a06f-cae9-433d-a229-dde3e68d6dc7 does not hold a content datastream of the requested
/// type"; CELEX 32003L0088 and 31995L0046 each listed <c>xhtml</c> and answered 404 to
/// <c>Accept: application/xhtml+xml</c>; CELEX 32006L0112 listed <c>html</c> and answered 404 to
/// <c>Accept: text/html</c>. That is why
/// <see cref="EuFormatBodyAdmission.BodyAdmitted"/> here means exactly "the office lists a wording
/// format we will attempt", never "this will succeed", and why the fetch step falls through the
/// ordered candidates instead of treating the first as final. A 404 of that datastream shape is
/// genuinely distinct from a bad token: an invalid Accept value answers 400 "Illegal accept header"
/// (retained probe lex-event-20260904T130647372Z-1d98471443364a779feba8c3a524cf69).
/// </para>
/// <para>
/// The listing is a union over every Expression of the Work, so it can name a format that exists
/// for some language or edition and not for the one this route requests. Observed on 2026-09-04:
/// 31995L0046, a 1995 act, lists <c>fmx4</c>, <c>xhtml</c>, <c>pdfa1a</c> and <c>pdfa1b</c> as well
/// as <c>html</c>, yet answers 404 to <c>Accept: application/xhtml+xml</c> on the plain work URI
/// with <c>Accept-Language: eng</c> and 200 to <c>text/html</c>. Only those two Accept tokens were
/// probed on that act, so nothing is claimed here about its fmx4, pdfa1a or pdfa1b; one listed type
/// answering 404 is already enough to establish that a listing entry is evidence a candidate is
/// worth attempting and nothing more.
/// </para>
/// <para>
/// KEEP, IMPROVE, REFUSE against v2 (<c>src/Lex.Sources.EurLex/EurLexAdapter.cs</c> in the v2
/// repository), stated here because this is the type that replaces v2's format handling.
/// </para>
/// <para>
/// KEEP v2's two publisher facts. First, that pre-XHTML acts are served as HTML (v2's own lines 249
/// and 407): reconfirmed live and made precise here, since the boundary is per work rather than per
/// era - 32003L0088 (2003) and 32004R0139 (2004) are served as HTML while 32005L0029 (2005) and
/// 32006L0112 (2006) are served as XHTML and answer 404 to <c>text/html</c>. Second, that Formex is
/// optional (v2's D48, its line 478): kept, and this slice goes no further than optional, leaving
/// Formex off the ladder entirely (see
/// <see cref="EuDocumentFetchAddress.TryMediaTypeFor"/> for the two reasons).
/// </para>
/// <para>
/// IMPROVE on v2 by deriving the ladder from the office's own listed manifestation types per work,
/// rather than sending one fixed multi-value Accept and recording whatever came back. v2 sent
/// <c>application/xhtml+xml, text/html</c> and could not say which of the two it had received, so
/// its record could not name the format it held. Here the candidate set and its order come from
/// family M's own delivered rows, every attempt carries exactly one Accept token, and the run names
/// the format actually served.
/// </para>
/// <para>
/// REFUSE v2's institutional-host fallback (its line 219), which guesses a second host when the
/// first does not answer. There is no canary for that host, Decision 23 excludes it, and this
/// route's own <see cref="EuDocumentFetchAddress.AdmittedHost"/> is a closed single-member set. A
/// listed candidate that does not serve falls through to the next LISTED candidate on the same
/// admitted host, and when none serves the object records its own typed
/// <c>RequestedRepresentationNotServed</c> rather than reaching for an unproven origin.
/// </para>
/// <para>
/// Contracts-only: nothing here calls a store or a publisher endpoint.
/// </para>
/// </remarks>
public static class EuManifestationListingDecode
{
    /// <summary>
    /// The closed preference order RULING
    /// lex-event-20260904T174138711Z-cdf5cbd17806423cbe05a6234cc4f262 fixes: XHTML, then html, then
    /// PDF/A, then PDF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All four ruled rungs are here. RULING
    /// lex-event-20260904T185339315Z-87d1510eccdc42a5947c41d2d8580744 admitted
    /// <see cref="EuManifestationMediaType.ApplicationPdf"/> as this route's tenth media type, so the
    /// PDF rung is real; the PDF/A rung is represented by <see cref="EuManifestationFormat.PdfA2a"/>
    /// alone, because pdfa1a and pdfa1b have never been observed serving and
    /// <see cref="EuDocumentFetchAddress.TryMediaTypeFor"/>'s own remarks record what was seen for
    /// each. Only formats that method can address are on the ladder, so a rung can never mint a
    /// request for an Accept token nobody has observed, and nothing here silently substitutes one
    /// PDF profile for another: requesting <c>application/pdf;type=pdfa2a</c> because a Work listed
    /// <c>pdfa1a</c> would ask for a representation the office never said it had.
    /// </para>
    /// <para>
    /// The order is a property of this vocabulary, not of any one Work: a Work's own candidates are
    /// this list filtered to what that Work's listing names, so two Works listing the same formats
    /// always attempt them in the same order.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<EuManifestationFormat> FormatLadder = Array.AsReadOnly(
        new[]
        {
            EuManifestationFormat.Xhtml,
            EuManifestationFormat.Html,
            EuManifestationFormat.PdfA2a,
            EuManifestationFormat.Pdf,
        });

    /// <summary>
    /// The exact lexical token the office's own <c>cdm:manifestation_type</c> literal carries for
    /// each closed format, and the only mapping this door admits.
    /// </summary>
    /// <remarks>
    /// Its own switch, deliberately not a reuse of <see cref="EuScopeProfile"/>'s own format tokens
    /// or of <see cref="EuManifestationFormat"/>'s wire names, for the identical reason
    /// <see cref="EuObjectFactsDiscoveryPlan.CdmIri"/> keeps its own switch rather than reusing a
    /// wire-token lookup: those are serialization concerns and this is a publisher-lexical concern
    /// that happens to share the same spelling today, so collapsing them would let a future wire
    /// rename silently change which publisher tokens this door recognises. Seven of the nine were
    /// observed as real listed values on 2026-09-04 (<c>fmx4</c>, <c>xhtml</c>, <c>html</c>,
    /// <c>pdf</c>, <c>pdfa1a</c>, <c>pdfa1b</c>, <c>print</c>); <c>xhtml5</c> and <c>pdfa2a</c> were
    /// offered by none of the eight Works whose listings this slice read (31995L0046, 32003L0087,
    /// 32003L0088, 32003R0001, 32004R0139, 32005L0029, 32006L0112, 32008R0593) and are admitted on
    /// the strength of <see cref="EuManifestationFormat"/>'s own closed vocabulary, which review/23
    /// section 1.2 grounds, not on an observation this slice took.
    /// </remarks>
    public static IReadOnlyDictionary<string, EuManifestationFormat> ListedTypeTokens { get; } =
        BuildTokenIndex();

    /// <summary>The only path that decodes family M's rows into per-object listings.</summary>
    /// <param name="closure">
    /// This call's own object set <c>O</c>, in Appendix A's exact canonical lexical form. A row
    /// naming a parent outside it is refused, never silently admitted, exactly as family P's and
    /// family X's own closure checks already refuse one.
    /// </param>
    /// <param name="listingRows">Family M's rows, already reopened and re-verified.</param>
    /// <param name="listingProfile">The interpretation profile <paramref name="listingRows"/> were verified under.</param>
    /// <param name="evidenceRef">
    /// Family M's OWN delivery evidence, per the SCOPE_RULING's second line: every disposition this
    /// produces names the observation it came from, never a sibling family's. The caller must pass
    /// family M's own interpretation-profile reference here, not the one it uses for families P and
    /// X; D1-05d's REVIEW_RESULT lex-event-20260904T192428840Z-a6a8ebd26c58436aafd109a55303c12e
    /// found this doc contradicted by the adapter, which stamped every format observation with
    /// family P's ref while M's own proof went unused, and
    /// <c>EuQueryExecutionAdapterTests.AMintedFormatDispositionNamesFamilyMsOwnDeliveryEvidence</c>
    /// now holds the two apart.
    /// </param>
    /// <param name="refusal">Why no listings were returned, when none were.</param>
    /// <param name="offendingIri">The exact canonical parent IRI a closure refusal names, else null.</param>
    /// <param name="offendingToken">The exact unknown manifestation type a vocabulary refusal names, else null.</param>
    /// <returns>
    /// One entry per object in <paramref name="closure"/> the rows say anything about, keyed by
    /// canonical IRI. An object whose only row is family M's explicit absence row is deliberately
    /// ABSENT from the result: the office listing nothing is the typed absence, and inventing an
    /// observation for it would turn "nobody offers this a body" into "we looked at a format". An
    /// object whose listing carried a token outside the closed vocabulary IS present, carrying
    /// <see cref="ObserveUnreadableListing"/>'s own answer, so the unreadable listing and the empty
    /// listing stay two distinguishable facts.
    /// </returns>
    public static IReadOnlyDictionary<string, EuFormatObservation>? TryDecode(
        IReadOnlySet<string> closure,
        IReadOnlyList<RepeatedEnumerationRow> listingRows,
        RepeatedEnumerationInterpretationProfile listingProfile,
        SourceArtifactRef evidenceRef,
        out EuManifestationListingRefusal refusal,
        out string? offendingIri,
        out string? offendingToken)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentNullException.ThrowIfNull(listingRows);
        ArgumentNullException.ThrowIfNull(listingProfile);
        ArgumentNullException.ThrowIfNull(evidenceRef);
        offendingIri = null;
        offendingToken = null;

        if (Array.Exists(listingRows.ToArray(), static row => row is null))
        {
            throw new ArgumentException("A listing row cannot be null.", nameof(listingRows));
        }

        var listedByParent = new Dictionary<string, SortedSet<EuManifestationFormat>>(StringComparer.Ordinal);
        var unadmittedTokenByParent = new Dictionary<string, string>(StringComparer.Ordinal);
        var absentParents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in listingRows)
        {
            var parentTerm = Term(row, listingProfile, "parent");
            var valueTerm = Term(row, listingProfile, "value");
            var valueKindTerm = Term(row, listingProfile, "value_kind");
            var datatypeTerm = Term(row, listingProfile, "datatype_iri");
            var languageTerm = Term(row, listingProfile, "language_tag");

            if (parentTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || parentTerm.Value is null ||
                !IsPlainLiteral(valueKindTerm) || !IsPlainLiteral(datatypeTerm) ||
                !IsPlainLiteral(languageTerm) ||
                (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (valueKindTerm.Value == "unbound"))
            {
                refusal = EuManifestationListingRefusal.ListingRowTermKindMismatch;
                return null;
            }

            var canonicalParent = EuPackRootCanonicalForm.TryCanonicalize(parentTerm.Value, out _);
            if (canonicalParent is null)
            {
                refusal = EuManifestationListingRefusal.ListingRowTermKindMismatch;
                return null;
            }

            if (!closure.Contains(canonicalParent))
            {
                refusal = EuManifestationListingRefusal.ListingParentNotInClosure;
                offendingIri = canonicalParent;
                return null;
            }

            if (valueKindTerm.Value == "unbound")
            {
                absentParents.Add(canonicalParent);
                continue;
            }

            // The publisher's own listing answers a plain xsd:string literal. A language-tagged or
            // IRI-valued manifestation type is a shape this door has never observed and refuses
            // rather than reinterprets: reading STR() off an IRI would silently invent a token.
            if (valueTerm.Kind != RepeatedEnumerationRdfTermKind.Literal || valueTerm.Value is null ||
                valueTerm.Language is not null)
            {
                refusal = EuManifestationListingRefusal.ListingRowTermKindMismatch;
                return null;
            }

            if (!listedByParent.TryGetValue(canonicalParent, out var listed))
            {
                listed = [];
                listedByParent[canonicalParent] = listed;
            }

            if (!ListedTypeTokens.TryGetValue(valueTerm.Value, out var format))
            {
                // Fix two: this Work's listing carried a type this vocabulary does not know. Record
                // it against THIS WORK and carry on; the run must not die because one object's
                // publisher vocabulary moved. The parent's entry above is created first on purpose,
                // so a Work whose listing is ENTIRELY unknown tokens is still distinguishable from a
                // Work the office lists nothing for.
                offendingToken ??= valueTerm.Value;
                if (!unadmittedTokenByParent.ContainsKey(canonicalParent))
                {
                    unadmittedTokenByParent[canonicalParent] = valueTerm.Value;
                }

                continue;
            }

            listed.Add(format);
        }

        foreach (var parent in absentParents)
        {
            if (listedByParent.ContainsKey(parent))
            {
                refusal = EuManifestationListingRefusal.ListingContradictsItsOwnAbsenceRow;
                offendingIri = parent;
                return null;
            }
        }

        var observations = new Dictionary<string, EuFormatObservation>(StringComparer.Ordinal);
        foreach (var (parent, listed) in listedByParent)
        {
            observations[parent] = unadmittedTokenByParent.TryGetValue(parent, out var unadmitted)
                ? ObserveUnreadableListing(listed, unadmitted, evidenceRef)
                : Observe(listed, evidenceRef);
        }

        refusal = EuManifestationListingRefusal.None;
        return observations;
    }

    /// <summary>
    /// The one rule that turns a Work's listed set into an observation. Exposed so the ladder's
    /// arms can be driven directly, and because it is the whole of D1-05d's format policy.
    /// </summary>
    /// <remarks>
    /// Three outcomes, and each is a different fact.
    /// <list type="bullet">
    /// <item>
    /// The listing names at least one format on <see cref="FormatLadder"/>: the candidates are those
    /// formats in ladder order, the observation's own <see cref="EuFormatObservation.Format"/> is the
    /// first of them (the one the manifest row's single address is minted for), and the admission is
    /// <see cref="EuFormatBodyAdmission.BodyAdmitted"/> - we will attempt these, in this order.
    /// </item>
    /// <item>
    /// The listing names <see cref="EuManifestationFormat.Print"/> and nothing else: print alone,
    /// which <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/> makes a permanent
    /// exclusion, so the body axis reaches <c>never_ingest</c>. No digital body can be read off
    /// paper under any later profile.
    /// </item>
    /// <item>
    /// The listing names something, but nothing this route can address as a wording body (Formex
    /// only, say, or Formex and print): a typed gap pending a reviewed profile, not a permanent
    /// exclusion, so the observed format is the listed one lowest in the closed vocabulary's own
    /// declared order and the admission is <see cref="EuFormatBodyAdmission.BodyNotAdmitted"/>. The
    /// body axis reads that as <c>typed_quarantine</c>, which is the honest answer: the office
    /// offers something, and this slice has no reviewed way to read it.
    /// </item>
    /// </list>
    /// A Work the office lists NOTHING for never reaches this method at all; see
    /// <see cref="TryDecode"/>'s own returns note.
    /// </remarks>
    public static EuFormatObservation Observe(
        IReadOnlyCollection<EuManifestationFormat> listedFormats, SourceArtifactRef evidenceRef)
    {
        ArgumentNullException.ThrowIfNull(listedFormats);
        ArgumentNullException.ThrowIfNull(evidenceRef);
        if (listedFormats.Count == 0)
        {
            throw new ArgumentException(
                "An empty listing is the typed absence and carries no format observation at all; " +
                "see EuManifestationListingDecode.TryDecode's own returns note.",
                nameof(listedFormats));
        }

        foreach (var format in listedFormats)
        {
            ContractValidation.RequireDefined(format, nameof(listedFormats));
        }

        var listed = new HashSet<EuManifestationFormat>(listedFormats);
        var candidates = FormatLadder.Where(listed.Contains).ToArray();
        if (candidates.Length > 0)
        {
            return new EuFormatObservation(
                candidates[0],
                EuFormatBodyAdmission.BodyAdmitted,
                "listing_offers_wording_format",
                evidenceRef,
                candidates);
        }

        if (listed.Count == 1 && listed.Contains(EuManifestationFormat.Print))
        {
            return new EuFormatObservation(
                EuManifestationFormat.Print,
                EuFormatBodyAdmission.BodyNotAdmitted,
                "listing_offers_print_only",
                evidenceRef);
        }

        return new EuFormatObservation(
            listed.Min(),
            EuFormatBodyAdmission.BodyNotAdmitted,
            "listing_offers_no_addressable_wording_format",
            evidenceRef);
    }

    /// <summary>
    /// The one guard both <see cref="EuFormatObservation"/> and <see cref="EuFormatDisposition"/>
    /// apply to an ordered candidate list, so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// A candidate list must be a subsequence of <see cref="FormatLadder"/> - every member on the
    /// ladder, no duplicates, strictly increasing in ladder position - and, when nonempty, must
    /// begin with the disposition's own single <c>Format</c>, which is the one address the manifest
    /// row carries and therefore the first attempt. An empty list is admitted, and means this value
    /// carries no per-object listing: the class-level policy rows
    /// <see cref="EuManifestationScope.Formats"/> closes over are exactly that, one policy row per
    /// format rather than one Work's own listing.
    /// </remarks>
    internal static IReadOnlyList<EuManifestationFormat> RequireCandidateLadder(
        IReadOnlyList<EuManifestationFormat>? candidates,
        EuManifestationFormat format,
        string paramName)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<EuManifestationFormat>());
        }

        var previousLadderPosition = -1;
        foreach (var candidate in candidates)
        {
            ContractValidation.RequireDefined(candidate, paramName);
            var position = -1;
            for (var index = 0; index < FormatLadder.Count; index++)
            {
                if (FormatLadder[index] == candidate)
                {
                    position = index;
                    break;
                }
            }

            if (position < 0)
            {
                throw new ArgumentException(
                    $"{candidate} is not a member of the closed fetch ladder, so it can never be a " +
                    "fetch candidate; see EuManifestationListingDecode.FormatLadder.",
                    paramName);
            }

            if (position <= previousLadderPosition)
            {
                throw new ArgumentException(
                    "Fetch candidates must be strictly increasing in the closed ladder's own order, " +
                    "with no repeats; a candidate list in any other order would attempt formats in " +
                    "an order no ruling fixed.",
                    paramName);
            }

            previousLadderPosition = position;
        }

        if (candidates[0] != format)
        {
            throw new ArgumentException(
                $"The first fetch candidate ({candidates[0]}) must be this value's own format " +
                $"({format}): the manifest row carries exactly one address, and it is the first " +
                "attempt.",
                paramName);
        }

        return Array.AsReadOnly(candidates.ToArray());
    }

    /// <summary>The reason-code prefix a Work whose listing this route could not fully read carries.</summary>
    public const string UnadmittedTypeReasonCode = "listing_type_not_admitted";

    /// <summary>
    /// One Work's observation when its listing named a manifestation type outside the closed
    /// vocabulary. Never admitted as a body source, so the body axis is a typed gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The admission and the reason carry the real fact: this route did not fully read this Work's
    /// listing, so it will not claim to have chosen a format from it, and it offers no fetch
    /// candidates. The reason names the exact offending token so a reader can see which one moved.
    /// </para>
    /// <para>
    /// The <see cref="EuFormatObservation.Format"/> field is descriptive only here, and it is chosen
    /// to avoid two different overclaims. It names the lowest-ordinal format the Work really listed
    /// EXCLUDING <see cref="EuManifestationFormat.Print"/>, because naming print would send the body
    /// axis to <c>never_ingest</c> through
    /// <see cref="EuManifestationScope.FormatsThatCanNeverCarryABody"/>, and a permanent exclusion is
    /// exactly what an unread listing does not license: the unknown token may well be a body format.
    /// When the Work listed no known non-print format at all (its listing is print plus unknowns, or
    /// unknowns alone) there is no publisher value left to name, so this names
    /// <see cref="EuManifestationFormat.NoneAdmitted"/>, the one member that means exactly that.
    /// It named <see cref="EuManifestationFormat.Formex4"/> until RULING
    /// lex-event-20260904T201230364Z-8afe287d7c9b49509a410204e7ee729d, which is a format the office
    /// had not listed for that Work; a field inventing a publisher fact is not excused by the branch
    /// being unreachable, since the branch exists to be correct when it is finally reached.
    /// </para>
    /// </remarks>
    public static EuFormatObservation ObserveUnreadableListing(
        IReadOnlyCollection<EuManifestationFormat> knownListedFormats,
        string unadmittedToken,
        SourceArtifactRef evidenceRef)
    {
        ArgumentNullException.ThrowIfNull(knownListedFormats);
        ArgumentException.ThrowIfNullOrWhiteSpace(unadmittedToken);
        ArgumentNullException.ThrowIfNull(evidenceRef);

        var namedFormat = EuManifestationFormat.NoneAdmitted;
        foreach (var candidate in knownListedFormats.OrderBy(static value => value))
        {
            ContractValidation.RequireDefined(candidate, nameof(knownListedFormats));
            if (candidate != EuManifestationFormat.Print)
            {
                namedFormat = candidate;
                break;
            }
        }

        return new EuFormatObservation(
            namedFormat,
            EuFormatBodyAdmission.BodyNotAdmitted,
            UnadmittedTypeReasonCode + ":" + BoundTokenForReason(unadmittedToken),
            evidenceRef);
    }

    /// <summary>
    /// The offending token, reduced to something a bounded printable-ASCII contract identifier can
    /// carry. The exact unreduced token always remains in family M's own retained rows, which are
    /// what this observation's evidence reference points at.
    /// </summary>
    private static string BoundTokenForReason(string token)
    {
        var builder = new System.Text.StringBuilder(MaximumReasonTokenLength);
        foreach (var character in token)
        {
            if (builder.Length == MaximumReasonTokenLength)
            {
                break;
            }

            builder.Append(
                character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '+' or '.'
                    ? character
                    : '_');
        }

        return builder.Length == 0 ? "unnamed" : builder.ToString();
    }

    private const int MaximumReasonTokenLength = 64;

    /// <summary>
    /// The publisher tokens, and only those. <see cref="EuManifestationFormat.NoneAdmitted"/> is
    /// excluded by construction: it is this vocabulary's own answer for a listing it cannot read,
    /// never something the office can say, so admitting a token for it would let a publisher literal
    /// decode straight into "none of what was listed is admitted".
    /// </summary>
    private static Dictionary<string, EuManifestationFormat> BuildTokenIndex()
    {
        var index = new Dictionary<string, EuManifestationFormat>(StringComparer.Ordinal);
        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            if (format == EuManifestationFormat.NoneAdmitted)
            {
                continue;
            }

            index.Add(ListedTypeToken(format), format);
        }

        return index;
    }

    private static string ListedTypeToken(EuManifestationFormat format) => format switch
    {
        EuManifestationFormat.Formex4 => "fmx4",
        EuManifestationFormat.Xhtml => "xhtml",
        EuManifestationFormat.Xhtml5 => "xhtml5",
        EuManifestationFormat.Html => "html",
        EuManifestationFormat.Pdf => "pdf",
        EuManifestationFormat.PdfA1a => "pdfa1a",
        EuManifestationFormat.PdfA1b => "pdfa1b",
        EuManifestationFormat.PdfA2a => "pdfa2a",
        EuManifestationFormat.Print => "print",
        EuManifestationFormat.NoneAdmitted => throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "NoneAdmitted is this vocabulary's own answer for an unreadable listing, never a token " +
            "the office can send; it has no publisher lexical form by construction."),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static bool IsPlainLiteral(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal && term.Datatype is null && term.Language is null;

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string variableName)
    {
        var index = -1;
        for (var candidate = 0; candidate < profile.ProjectionVariables.Count; candidate++)
        {
            if (string.Equals(profile.ProjectionVariables[candidate], variableName, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            throw new ArgumentException(
                $"'{variableName}' is not part of this profile's projection.", nameof(variableName));
        }

        if (index >= row.Terms.Count)
        {
            throw new ArgumentException(
                $"A row has {row.Terms.Count} term(s), too few to read '{variableName}' at " +
                $"projection position {index}.",
                nameof(row));
        }

        return row.Terms[index];
    }
}
