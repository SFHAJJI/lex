using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why <see cref="EuLanguageScopedExpressionDerivation.TryDerive"/> produced no derivation. Closed at
/// one, and one is the honest size.
/// </summary>
/// <remarks>
/// This door performs no validation of its own beyond what
/// <see cref="EuLanguageScopedExpressionDecode.TryDecode"/> already performs, so it has exactly one
/// refusal and that refusal forwards. A second member was drafted and removed; see
/// <see cref="EuLanguageScopedExpressionDerivation"/>'s own remarks on what could not be checked and
/// why inventing a check for it would have been worse than naming the limit.
/// </remarks>
public enum EuLanguageScopedExpressionDerivationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// <see cref="EuLanguageScopedExpressionDecode.TryDecode"/> itself refused. Its own named
    /// refusal travels beside this one rather than being flattened into it: this door adds no
    /// vocabulary for conditions the decoder already names exactly.
    /// </summary>
    [JsonStringEnumMemberName("decode_refused")]
    DecodeRefused = 1,
}

/// <summary>
/// One retainable, digest-addressable record of the language-scoped expressions decoded from named
/// enumeration proofs, bound to those proofs.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS RATHER THAN A FIELD ON AN EXISTING RECORD. #418's completion boundary
/// requires the decoded expressions to be bound into the additive corpus/derivation envelope.
/// <see cref="Lex.V3.Contracts.Source.Corpus.CorpusRecord"/> has no slot for them -- it carries a
/// schema, an object reference and ordinal, four dispositions, a body record, a manifest reference
/// and a run identity, and nothing else -- and widening it would change a reviewed schema id inside
/// the corpus/6 surface another seat is actively building. Additive was the instruction and additive
/// is also what keeps this out of that surface's way, so the expressions are retained as their own
/// artifact that a corpus record can later reference, rather than inside one.
/// </para>
/// <para>
/// THE CALLER CANNOT SUPPLY THE EXPRESSIONS, AND THAT IS THE WHOLE DESIGN. There is no constructor
/// taking a <see cref="LanguageScopedExpressionSet"/>. <see cref="TryDerive"/> takes the same
/// proof-bound deliveries <see cref="EuLanguageScopedExpressionDecode.TryDecode"/> takes, mints a
/// fresh set, and decodes into it. So "these expressions came from these proofs" is a fact about one
/// call rather than a pairing a caller asserted.
/// </para>
/// <para>
/// This matters because the alternative was tried twice in this repository and rejected twice.
/// <c>EuProcedureEventProducer</c>'s own remarks record that its <c>DecodeRows</c> was public and
/// took "a caller-supplied row list and a caller-supplied evidence reference, so observations could
/// be minted from rows nobody had proven, citing custody nobody had established"; the same defect
/// was then reintroduced in the expression decoder by not reading that precedent first. A
/// <c>FromDecodedSet(runRef, proofs, expressions)</c> door here would be the third occurrence, and
/// no amount of documentation would make it honest.
/// </para>
/// <para>
/// THE TWO DELIVERIES ARE NOT ESTABLISHED TO BE ONE EPISODE, AND THIS TYPE DOES NOT PRETEND THEY
/// ARE. This paragraph replaces a check that was written, and removed once it was measured rather
/// than assumed. The draft refused a pairing whose two proofs carried different
/// <c>AcquisitionRunRef</c> values, on the reasoning that family P's dates would otherwise be
/// attached to family X's expressions across two different observations. The reasoning is sound and
/// the check was useless: <c>RoutedHttpAcquisitionSession</c> mints a run identity from a fresh
/// <c>Guid.NewGuid()</c> per session, each family partition runs in its own session, so two families
/// ALWAYS carry different run references. The check would have refused every legitimate pairing and
/// admitted none.
/// </para>
/// <para>
/// The session's <c>adapterExecutionIdentity</c> was the obvious second candidate and fails for the
/// same reason -- it is minted per session too -- and it does not reach a proof in any case. So
/// nothing in this build binds two family deliveries to one acquisition episode, and the correct
/// response is to say so rather than to ship a differently-named check that does not check it.
/// Consequently this type publishes no single <c>AcquisitionRunRef</c>: each proof carries its own,
/// both are readable, and a reader who needs them to be one episode has to establish that from
/// something other than this record.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CLAIM. It does not claim to hold every expression of any work. Its scope is
/// exactly the deliveries named in it: family X over whatever batch that run asked about. An empty
/// <see cref="Expressions"/> means the named deliveries stated no expressions this decoder admits --
/// it does not mean the publisher holds none, and there is no door here by which a caller could
/// upgrade the first reading into the second.
/// </para>
/// </remarks>
public sealed class EuLanguageScopedExpressionDerivation
{
    /// <summary>The digest schema for a whole derivation.</summary>
    private const string CanonicalSchema = "eu_language_scoped_expression_derivation/1";

    private EuLanguageScopedExpressionDerivation(
        AbsenceFamilyEnumerationProof expressionFactsProof,
        AbsenceFamilyEnumerationProof? objectFactsProof,
        IReadOnlyList<LanguageScopedExpression> expressions,
        byte[] canonicalBytes)
    {
        ExpressionFactsProof = expressionFactsProof;
        ObjectFactsProof = objectFactsProof;
        Expressions = expressions;
        CanonicalBytes = canonicalBytes;
        CanonicalSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
    }

    /// <summary>The schema every derivation declares.</summary>
    public static string Schema => CanonicalSchema;

    /// <summary>The family X (Expression-facts) enumeration proof these expressions were read from.</summary>
    public AbsenceFamilyEnumerationProof ExpressionFactsProof { get; }

    /// <summary>
    /// The family P (object-facts) enumeration proof the corrigendum dates were read from, or
    /// <c>null</c> when this derivation was made without one.
    /// </summary>
    /// <remarks>
    /// Null here and an expression carrying no date are different facts and are recorded as
    /// different facts: the first says no date delivery was consulted, the second says one was and
    /// stated nothing for that work. Collapsing them would make "we never asked" and "we asked and
    /// there was none" the same record, which is the absence doctrine this build holds everywhere
    /// else.
    /// </remarks>
    public AbsenceFamilyEnumerationProof? ObjectFactsProof { get; }

    /// <summary>Every expression derived, in the order the publisher first stated it.</summary>
    public IReadOnlyList<LanguageScopedExpression> Expressions { get; }

    /// <summary>The canonical bytes to retain.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes { get; }

    /// <summary>The digest of <see cref="CanonicalBytes"/>.</summary>
    public string CanonicalSha256 { get; }

    /// <summary>
    /// Decodes both deliveries into a fresh set and records the result, or refuses without recording.
    /// </summary>
    /// <param name="expressionFacts">The family X delivery, in the proof-bound shape the decoder requires.</param>
    /// <param name="objectFacts">The family P delivery, or <c>null</c> when this derivation consults none.</param>
    /// <param name="refusal">This door's own refusal, or <c>None</c>.</param>
    /// <param name="decodeRefusal">
    /// The decoder's own refusal when <paramref name="refusal"/> is <c>DecodeRefused</c>.
    /// </param>
    /// <param name="detail">The decoder's own detail, when it supplied one.</param>
    /// <param name="offendingIri">The IRI a decoder refusal is about, when it has one.</param>
    public static EuLanguageScopedExpressionDerivation? TryDerive(
        EuProofBoundDelivery expressionFacts,
        EuProofBoundDelivery? objectFacts,
        out EuLanguageScopedExpressionDerivationRefusal refusal,
        out EuLanguageScopedExpressionDecodeRefusal decodeRefusal,
        out string? detail,
        out string? offendingIri)
    {
        ArgumentNullException.ThrowIfNull(expressionFacts);

        refusal = EuLanguageScopedExpressionDerivationRefusal.None;

        // WHAT THIS CALL APPENDED, NOT WHAT THE SET HOLDS. The set is minted here and handed to
        // nothing else, so today the two are the same list. They are not the same CLAIM: the set
        // says "these expressions are held", the return value says "this decode produced these".
        // A mutation run while writing this found the difference is observable -- truncating the
        // decoder's return left the set intact and the test still passed -- so the record now cites
        // the narrower of the two, which is the one its own remarks describe.
        var set = new LanguageScopedExpressionSet();
        var appended = EuLanguageScopedExpressionDecode.TryDecode(
            expressionFacts, objectFacts, set, out decodeRefusal, out detail, out offendingIri);
        if (appended is null)
        {
            refusal = EuLanguageScopedExpressionDerivationRefusal.DecodeRefused;
            return null;
        }

        var canonical = ContractCanonicalizer.Canonicalize(
            new CanonicalDerivationDocument(
                CanonicalSchema,
                CanonicalProofDocument.Of(expressionFacts.Proof),
                objectFacts is null ? null : CanonicalProofDocument.Of(objectFacts.Proof),
                [.. appended.Select(CanonicalExpressionDocument.Of)]),
            CanonicalSchema + "-canonical-json",
            64);

        return new EuLanguageScopedExpressionDerivation(
            expressionFacts.Proof,
            objectFacts?.Proof,
            appended,
            canonical);
    }

    private sealed record CanonicalDerivationDocument(
        string Schema,
        CanonicalProofDocument ExpressionFactsProof,
        CanonicalProofDocument? ObjectFactsProof,
        IReadOnlyList<CanonicalExpressionDocument> Expressions);

    /// <summary>
    /// One enumeration proof, whole.
    /// </summary>
    /// <remarks>
    /// EVERY FIELD OF EVERY PROOF TRAVELS, not a chosen subset. #584's review found the same
    /// omission one type over: a comparison read four of a proof's seven fields, so a proof retained
    /// under the weaker custody class replayed as identical to a floored one. A derivation digest
    /// that skipped <c>RetainedFloor</c> or either profile reference would make two genuinely
    /// different derivations share an address, which is worse here than there because this address
    /// is what the artifact is stored and reopened under.
    /// </remarks>
    private sealed record CanonicalProofDocument(
        string FamilyKey,
        string AcquisitionRunResourceId,
        string AcquisitionRunSha256,
        string InterpretationProfileResourceId,
        string InterpretationProfileSha256,
        string SourceProfileResourceId,
        string SourceProfileSha256,
        long DeliveredRowCount,
        string CanonicalKeyDigest,
        string RetainedFloor)
    {
        public static CanonicalProofDocument Of(AbsenceFamilyEnumerationProof proof) =>
            new(
                proof.FamilyKey,
                proof.AcquisitionRunRef.ResourceId,
                proof.AcquisitionRunRef.Sha256,
                proof.InterpretationProfileRef.ResourceId,
                proof.InterpretationProfileRef.Sha256,
                proof.SourceProfileRef.ResourceId,
                proof.SourceProfileRef.Sha256,
                proof.DeliveredRowCount,
                proof.CanonicalKeyDigest,
                proof.RetainedFloor.ToString());
    }

    /// <summary>
    /// One expression's admitted content and the retained bytes it was derived from.
    /// </summary>
    /// <remarks>
    /// The lineage travels as the receipts' own content digests, in the order the expression holds
    /// them. <see cref="LanguageScopedExpression.CanonicalContentSha256"/> deliberately does NOT
    /// cover the lineage -- its own remarks record why: two runs that paged differently agree on
    /// what the publisher said and differ only in which bodies carried it. That is the right rule
    /// for comparing two expressions. It is the wrong rule for addressing a stored artifact, which
    /// must change when the bytes it cites change, so the lineage is digested here and not there.
    /// </remarks>
    private sealed record CanonicalExpressionDocument(
        string PublisherWorkId,
        string PublisherExpressionId,
        string OfficialLanguage,
        string? PublisherDateRawLexical,
        string? PublisherDateDatatypeIri,
        string SourceObjectCanonicalKeySha256,
        string CanonicalContentSha256,
        IReadOnlyList<string> LineageContentSha256InOrder)
    {
        public static CanonicalExpressionDocument Of(LanguageScopedExpression expression) =>
            new(
                expression.Identity.PublisherWorkId,
                expression.Identity.PublisherExpressionId,
                expression.OfficialLanguage,
                expression.PublisherCorrigendumDate?.RawLexical,
                expression.PublisherCorrigendumDate?.DatatypeIri,
                expression.SourceObject.CanonicalKeySha256,
                expression.CanonicalContentSha256,
                [.. expression.Lineage.Entries.Select(static entry => entry.ContentSha256)]);
    }
}
