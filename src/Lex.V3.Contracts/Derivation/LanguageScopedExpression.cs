using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Derivation;

/// <summary>Why an expression could not be appended to a set that already holds its identity.</summary>
/// <remarks>
/// Closed at two members because an append has exactly two failing shapes to distinguish and one
/// succeeding one. Re-presenting an identity whose admitted content is the same is not a failure at
/// all - it is the idempotence an append-only store must have to survive a replay - so it is
/// <see cref="None"/> rather than a refusal, and the set's count does not grow. Content, not bytes:
/// a replay that paged differently states the same thing and must converge, not conflict.
/// </remarks>
public enum LanguageScopedExpressionAppendRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The same expression identity was presented saying something different.</summary>
    [JsonStringEnumMemberName("conflicting_canonical_content")]
    ConflictingCanonicalContent = 1,
}

/// <summary>The publisher's own corrigendum date, exactly as the publisher wrote it.</summary>
/// <remarks>
/// <para>
/// CARRIED, NEVER COMPUTED. The lexical form and its datatype travel together because a date's
/// precision is a property of the datatype the publisher chose, and widening it here would invent
/// precision the source never claimed - the same discipline the procedure-event observation already
/// holds for <c>event_legal_date</c>.
/// </para>
/// <para>
/// ABSENCE IS THE ABSENCE OF THIS OBJECT. When the publisher supplied no date there is no instance,
/// and no default, sentinel or run date stands in for one. A synthesized date is indistinguishable
/// from an observed one once it is written down, which is why none is ever produced.
/// </para>
/// </remarks>
public sealed record PublisherCorrigendumDate
{
    public PublisherCorrigendumDate(string rawLexical, string datatypeIri)
    {
        RawLexical = ContractValidation.RequireIdentifier(rawLexical, nameof(rawLexical));
        DatatypeIri = ContractValidation.RequireIdentifier(datatypeIri, nameof(datatypeIri));
    }

    /// <summary>The date exactly as the publisher wrote it, unparsed and unnormalized.</summary>
    public string RawLexical { get; }

    /// <summary>The datatype the publisher gave that literal. Never widened, never guessed.</summary>
    public string DatatypeIri { get; }
}

/// <summary>
/// One expression's identity: the publisher's expression identity within its publisher work.
/// </summary>
/// <remarks>
/// <para>
/// LANGUAGE IS NOT IN THE KEY, AND THAT IS THE WHOLE POINT. B31-L0071 locates E9's defect in a
/// "writer's language-keyed merge" that left the corpus unable to represent a second same-language
/// expression in one version directory. A language-keyed identity is that defect; this build has no
/// language or expression carrier yet, so it has the chance never to acquire one.
/// </para>
/// <para>
/// Nor is the corrigendum date in the key. Two corrigenda to one work in one language on one date
/// are two expressions, and a key that folded them would lose the second exactly as the language key
/// lost the second language.
/// </para>
/// <para>
/// Both components are carried as the publisher stated them. Nothing here is publisher-specific: a
/// Luxembourg rectificatif is its own act with its own Memorial number, while an EU corrigendum is a
/// language-scoped R(nn) expression of a work, and an identity that privileged either shape could
/// not carry the other.
/// </para>
/// </remarks>
public sealed record LanguageScopedExpressionIdentity
{
    public LanguageScopedExpressionIdentity(string publisherWorkId, string publisherExpressionId)
    {
        PublisherWorkId = ContractValidation.RequireIdentifier(
            publisherWorkId, nameof(publisherWorkId));
        PublisherExpressionId = ContractValidation.RequireIdentifier(
            publisherExpressionId, nameof(publisherExpressionId));
    }

    /// <summary>The publisher's identifier for the work this expression belongs to.</summary>
    public string PublisherWorkId { get; }

    /// <summary>The publisher's identifier for this expression within that work.</summary>
    public string PublisherExpressionId { get; }
}

/// <summary>
/// Which part of an expression a retained transport artifact actually contributed.
/// </summary>
/// <remarks>
/// <para>
/// A LINEAGE THAT DOES NOT SAY WHAT EACH ARTIFACT CONTRIBUTED IS A PILE OF DIGESTS. An expression's
/// identity and language are stated by one publisher query family; its date, when the publisher
/// states one at all, comes from a different family with its own delivery, its own pages and its own
/// retained bytes. Recording both without distinguishing them would let a reader believe the date
/// was witnessed by the bytes that named the expression, which is exactly the claim nothing here can
/// make.
/// </para>
/// <para>
/// Closed at two members because this contract knows of exactly two contributions: what makes an
/// expression the expression it is, and the publisher's date for the work it belongs to.
/// </para>
/// </remarks>
public enum LanguageScopedExpressionContribution
{
    /// <summary>Bytes that stated the expression's identity and its language.</summary>
    [JsonStringEnumMemberName("identity_and_language")]
    IdentityAndLanguage = 1,

    /// <summary>Bytes that stated the publisher's date for this expression's work.</summary>
    [JsonStringEnumMemberName("publisher_date")]
    PublisherDate = 2,
}

/// <summary>One retained transport artifact, and what it contributed to an expression.</summary>
public sealed record LanguageScopedExpressionLineageEntry
{
    public LanguageScopedExpressionLineageEntry(
        LanguageScopedExpressionContribution contribution,
        DurableBlobWriteReceipt retainedTransportBytes)
    {
        Contribution = ContractValidation.RequireDefined(contribution, nameof(contribution));
        RetainedTransportBytes = retainedTransportBytes
            ?? throw new ArgumentNullException(nameof(retainedTransportBytes));
    }

    /// <summary>What these bytes contributed.</summary>
    public LanguageScopedExpressionContribution Contribution { get; }

    /// <summary>The custody receipt for the exact retained bytes.</summary>
    public DurableBlobWriteReceipt RetainedTransportBytes { get; }

    /// <summary>The content address of those bytes.</summary>
    public string ContentSha256 => RetainedTransportBytes.Reference.ContentSha256;
}

/// <summary>
/// The complete set of retained transport artifacts an expression was derived from.
/// </summary>
/// <remarks>
/// <para>
/// COMPLETE, NOT REPRESENTATIVE. A delivery that arrived over four pages was witnessed by four
/// retained bodies, and naming one of them as "the" evidence would be choosing a witness. Every
/// contributing artifact is held, which is the only form in which the claim "this is where it came
/// from" survives a reader checking it.
/// </para>
/// <para>
/// ORDERED BY WHAT IT IS, NEVER BY HOW IT ARRIVED. Entries are deduplicated on
/// (contribution, content address) and ordered by the same pair, so re-paging the same delivery -
/// four pages instead of two, or the same pages reopened in a different order - yields an identical
/// lineage. Page structure is transport, and transport must not leak into a value that gets
/// compared.
/// </para>
/// <para>
/// This is provenance and it is deliberately NOT identity. What makes two expressions the same
/// expression is <see cref="LanguageScopedExpression.CanonicalContentSha256"/>, computed from what
/// the publisher said rather than from which bodies carried it.
/// </para>
/// </remarks>
public sealed class LanguageScopedExpressionLineage
{
    private readonly ReadOnlyCollection<LanguageScopedExpressionLineageEntry> _entries;

    private LanguageScopedExpressionLineage(
        ReadOnlyCollection<LanguageScopedExpressionLineageEntry> entries) => _entries = entries;

    /// <summary>Every contributing artifact, deduplicated and in canonical order.</summary>
    public IReadOnlyList<LanguageScopedExpressionLineageEntry> Entries => _entries;

    /// <summary>Whether any artifact here contributed a publisher date.</summary>
    public bool CarriesDateContribution =>
        _entries.Any(static entry =>
            entry.Contribution == LanguageScopedExpressionContribution.PublisherDate);

    /// <summary>
    /// The only door. At least one identity-and-language contribution is required, because an
    /// expression nothing witnessed the identity of is not an observation.
    /// </summary>
    public static LanguageScopedExpressionLineage FromContributions(
        IEnumerable<LanguageScopedExpressionLineageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var seen = new HashSet<(LanguageScopedExpressionContribution, string)>();
        var kept = new List<LanguageScopedExpressionLineageEntry>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (seen.Add((entry.Contribution, entry.ContentSha256)))
            {
                kept.Add(entry);
            }
        }

        if (!kept.Any(static entry =>
            entry.Contribution == LanguageScopedExpressionContribution.IdentityAndLanguage))
        {
            throw new ArgumentException(
                "A lineage must carry at least one identity-and-language contribution.",
                nameof(entries));
        }

        kept.Sort(static (left, right) =>
        {
            var byContribution = ((int)left.Contribution).CompareTo((int)right.Contribution);
            return byContribution != 0
                ? byContribution
                : string.CompareOrdinal(left.ContentSha256, right.ContentSha256);
        });

        return new LanguageScopedExpressionLineage(kept.AsReadOnly());
    }
}

/// <summary>
/// One language-scoped expression, bound at construction to the source evidence that produced it.
/// </summary>
/// <remarks>
/// <para>
/// PROVENANCE BY CONSTRUCTION, NEVER BY PARAMETER. The only door takes the source object and the
/// exact retained transport-byte receipt, and there is no parameter by which a caller asserts that
/// an expression is publisher-backed. A boolean saying so would be a caller's claim about evidence
/// rather than the evidence, and this contract has no way to check such a claim - so it does not
/// accept one.
/// </para>
/// <para>
/// The retained receipt is what makes the binding exact rather than nominal: it names the bytes as
/// custody wrote and read them back, so two presentations of one identity can be compared on what
/// was actually retained instead of on a caller-supplied digest.
/// </para>
/// </remarks>
public sealed class LanguageScopedExpression
{
    /// <summary>The digest schema for one expression's own admitted content.</summary>
    private const string CanonicalContentSchema = "language_scoped_expression_content/1";

    private LanguageScopedExpression(
        LanguageScopedExpressionIdentity identity,
        string officialLanguage,
        PublisherCorrigendumDate? publisherCorrigendumDate,
        SourceObjectRef sourceObject,
        LanguageScopedExpressionLineage lineage)
    {
        Identity = identity;
        OfficialLanguage = officialLanguage;
        PublisherCorrigendumDate = publisherCorrigendumDate;
        SourceObject = sourceObject;
        Lineage = lineage;
        CanonicalContentSha256 = ComputeCanonicalContentSha256(
            identity, officialLanguage, publisherCorrigendumDate);
    }

    /// <summary>The publisher expression identity within its publisher work.</summary>
    public LanguageScopedExpressionIdentity Identity { get; }

    /// <summary>
    /// The official language this expression is in, carried as separate data rather than as identity.
    /// </summary>
    public string OfficialLanguage { get; }

    /// <summary>
    /// The publisher's corrigendum date, or <c>null</c> when the publisher supplied none.
    /// </summary>
    /// <remarks>
    /// Null is the only representation of absence, and it is never replaced by a default. A reader
    /// that needs to distinguish "no corrigendum date" from "a corrigendum date this build could not
    /// read" will not find that distinction here, because nothing in this slice produces the second
    /// case: an unreadable publisher date refuses at <see cref="PublisherCorrigendumDate"/>'s own
    /// construction rather than arriving as an absence.
    /// </remarks>
    public PublisherCorrigendumDate? PublisherCorrigendumDate { get; }

    /// <summary>The source object this expression was derived from.</summary>
    public SourceObjectRef SourceObject { get; }

    /// <summary>
    /// Every retained transport artifact this expression was derived from, complete.
    /// </summary>
    public LanguageScopedExpressionLineage Lineage { get; }

    /// <summary>
    /// What two presentations of one identity are compared on: a digest over what the publisher
    /// said, never over which bodies carried it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS REPLACED A PAGE-BLOB DIGEST, AND THE REPLACEMENT FIXED A REAL DEFECT. An earlier shape
    /// used the retained page body's own content address as the expression's canonical bytes. That
    /// made semantic identity depend on transport structure: the same delivery re-fetched under a
    /// different page limit lands the same rows in different bodies, so two equivalent executions
    /// produced two different "canonical bytes" for one expression and the append-only set read
    /// that as a conflict. Worse, an expression carrying a date derived from a second query family
    /// was compared on bytes that never mentioned the date.
    /// </para>
    /// <para>
    /// What is digested here is exactly the admitted content - publisher work, publisher expression,
    /// language, and the date's raw lexical form and datatype when there is one - canonicalized
    /// under a named schema. Two runs that observed the same publisher statement agree; a run that
    /// observed a different language, a different date, or a date where there was none, does not.
    /// </para>
    /// </remarks>
    public string CanonicalContentSha256 { get; }

    /// <summary>
    /// The only door. Every argument is either validated here or carried from a verified boundary.
    /// </summary>
    /// <param name="lineage">
    /// The complete contributing transport-byte lineage. It must carry a date contribution when
    /// <paramref name="publisherCorrigendumDate"/> is present and must not when it is absent: a date
    /// no retained artifact witnessed, or retained date bytes behind an expression claiming no date,
    /// are both provenance this contract refuses to state.
    /// </param>
    public static LanguageScopedExpression FromRetainedSource(
        LanguageScopedExpressionIdentity identity,
        string officialLanguage,
        PublisherCorrigendumDate? publisherCorrigendumDate,
        SourceObjectRef sourceObject,
        LanguageScopedExpressionLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(lineage);

        if (lineage.CarriesDateContribution != (publisherCorrigendumDate is not null))
        {
            throw new ArgumentException(
                publisherCorrigendumDate is null
                    ? "A lineage carrying a publisher-date contribution needs a date to witness."
                    : "A publisher date needs the retained bytes that stated it.",
                nameof(lineage));
        }

        return new LanguageScopedExpression(
            identity,
            ContractValidation.RequireIdentifier(officialLanguage, nameof(officialLanguage)),
            publisherCorrigendumDate,
            sourceObject,
            lineage);
    }

    private static string ComputeCanonicalContentSha256(
        LanguageScopedExpressionIdentity identity,
        string officialLanguage,
        PublisherCorrigendumDate? publisherCorrigendumDate) =>
        Convert.ToHexString(
            SHA256.HashData(
                ContractCanonicalizer.Canonicalize(
                    new CanonicalContentDocument(
                        CanonicalContentSchema,
                        identity.PublisherWorkId,
                        identity.PublisherExpressionId,
                        officialLanguage,
                        publisherCorrigendumDate?.RawLexical,
                        publisherCorrigendumDate?.DatatypeIri),
                    CanonicalContentSchema + "-canonical-json",
                    64)))
            .ToLowerInvariant();

    private sealed record CanonicalContentDocument(
        string Schema,
        string PublisherWorkId,
        string PublisherExpressionId,
        string OfficialLanguage,
        string? PublisherDateRawLexical,
        string? PublisherDateDatatypeIri);
}

/// <summary>
/// An append-only set in which same-language expressions of one work coexist.
/// </summary>
/// <remarks>
/// <para>
/// FORWARD-ONLY, BECAUSE REWRITING AN APPEND-ONLY STORE BREAKS ITS CHAINS. Nothing here removes,
/// replaces or edits an admitted expression; the only operation adds. That is the shape
/// <c>30-FINAL-VERDICT.md</c> disposes for E9 - a forward-only dual layout with the old layout
/// frozen - and it is why this set stands beside the frozen <c>corpus/6</c> record rather than
/// altering it.
/// </para>
/// <para>
/// IDEMPOTENT ON IDENTICAL CONTENT, REFUSING ON CONFLICT. Re-presenting an identity whose admitted
/// content matches is accepted and changes nothing, so a replayed run converges instead of growing -
/// including a replay that arrived over a different number of pages, which is why the comparison is
/// on content rather than on the bytes that carried it. Re-presenting the same identity saying
/// something different is the one thing an append-only store must not silently absorb, and it
/// refuses by name.
/// </para>
/// <para>
/// THE FIRST ADMITTED LINEAGE STANDS. An idempotent re-presentation does not rewrite the provenance
/// already held, because this store never rewrites anything. Two runs that paged differently agree
/// on what the publisher said and differ only in which bodies carried it; the set keeps the lineage
/// it admitted and does not pretend to have witnessed both.
/// </para>
/// <para>
/// WHAT COEXISTENCE MEANS HERE, STATED SO IT IS NOT READ AS MORE. Two distinct expression identities
/// coexist even when their work, language and corrigendum date are all equal. This set makes no
/// claim that it holds every expression of a work, and there is no door by which a caller could
/// assert that it does.
/// </para>
/// </remarks>
public sealed class LanguageScopedExpressionSet
{
    private readonly List<LanguageScopedExpression> _expressions = [];
    private readonly Dictionary<LanguageScopedExpressionIdentity, string> _contentByIdentity = [];
    private readonly ReadOnlyCollection<LanguageScopedExpression> _exposedExpressions;

    public LanguageScopedExpressionSet() => _exposedExpressions = _expressions.AsReadOnly();

    /// <summary>Every admitted expression, in the order it was admitted.</summary>
    /// <remarks>
    /// <para>
    /// A GENUINE READ-ONLY WRAPPER, NOT THE BACKING LIST BEHIND A READ-ONLY INTERFACE. Returning
    /// <c>_expressions</c> directly would change only the compile-time view: a caller could cast the
    /// result back to <c>List&lt;T&gt;</c> or <c>ICollection&lt;T&gt;</c> and add, remove or clear
    /// entries. That would defeat the append-only guarantee this type exists for, and worse, leave
    /// the list disagreeing with the identity index that decides idempotence and conflict - so a
    /// removed expression's identity would still refuse a later, different presentation.
    /// </para>
    /// <para>
    /// The wrapper is built once in the constructor rather than per access, and it is a live view
    /// rather than a snapshot: appends appear through it, which is what a reader of an append-only
    /// store expects, and what a defensive copy would silently take away.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LanguageScopedExpression> Expressions => _exposedExpressions;

    /// <summary>
    /// Appends one expression, or refuses by name. Never removes, replaces or edits.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="true"/> both when the expression is newly admitted and when an
    /// identical one was already held: the caller asked for the store to contain this expression,
    /// and after either outcome it does. The two are distinguishable by
    /// <see cref="Expressions"/>'s count, which grows only on the first.
    /// </remarks>
    public bool TryAppend(
        LanguageScopedExpression expression,
        out LanguageScopedExpressionAppendRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (_contentByIdentity.TryGetValue(expression.Identity, out var heldContent))
        {
            if (!string.Equals(heldContent, expression.CanonicalContentSha256, StringComparison.Ordinal))
            {
                refusal = LanguageScopedExpressionAppendRefusal.ConflictingCanonicalContent;
                return false;
            }

            refusal = LanguageScopedExpressionAppendRefusal.None;
            return true;
        }

        _contentByIdentity.Add(expression.Identity, expression.CanonicalContentSha256);
        _expressions.Add(expression);
        refusal = LanguageScopedExpressionAppendRefusal.None;
        return true;
    }
}
