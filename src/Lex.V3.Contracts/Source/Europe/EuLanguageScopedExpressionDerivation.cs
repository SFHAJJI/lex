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
    /// <summary>The digest schema for what was derived. Byte-stable across executions.</summary>
    private const string DerivationSchema = "eu_language_scoped_expression_derivation/1";

    /// <summary>The digest schema for which execution observed it.</summary>
    private const string EpisodeSchema = "eu_language_scoped_expression_derivation_episode/1";

    private EuLanguageScopedExpressionDerivation(
        AbsenceFamilyEnumerationProof expressionFactsProof,
        AbsenceFamilyEnumerationProof? objectFactsProof,
        IReadOnlyList<LanguageScopedExpression> expressions,
        byte[] derivationBytes,
        byte[] episodeBytes)
    {
        ExpressionFactsProof = expressionFactsProof;
        ObjectFactsProof = objectFactsProof;
        Expressions = expressions;
        DerivationBytes = derivationBytes;
        DerivationSha256 = Convert.ToHexStringLower(SHA256.HashData(derivationBytes));
        EpisodeBytes = episodeBytes;
        EpisodeSha256 = Convert.ToHexStringLower(SHA256.HashData(episodeBytes));
    }

    /// <summary>The schema a derivation declares.</summary>
    public static string Schema => DerivationSchema;

    /// <summary>The schema an episode record declares.</summary>
    public static string EpisodeRecordSchema => EpisodeSchema;

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

    /// <summary>
    /// WHAT WAS DERIVED, canonicalized. Byte-identical for two independent executions that observed
    /// the same publisher statements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// S3-A04 REQUIRES BOTH HALVES AND THEY ARE NOT IN TENSION: "Derivation is byte-stable across
    /// two independent executions and every object retains source-observation and transport-byte
    /// lineage." The lineage is here - every expression carries its retained page receipts, and
    /// those digests are a function of the bytes the publisher sent, so identical bytes agree and
    /// different bytes do not. What is NOT here is which run fetched them.
    /// </para>
    /// <para>
    /// THE FIRST HEAD OF THIS TYPE FAILED THAT, AND IT FAILED IT THE WAY THIS REPOSITORY HAD ALREADY
    /// LEARNED ONCE. <see cref="LanguageScopedExpression.CanonicalContentSha256"/>'s own remarks
    /// record replacing a page-blob digest because it "made semantic identity depend on transport
    /// structure". The derivation digest then covered each proof's <c>AcquisitionRunRef</c>, which
    /// <c>RoutedHttpAcquisitionSession</c> mints from a fresh <c>Guid.NewGuid()</c> per session, so
    /// two identical runs addressed one derivation two ways. Review measured it: the same rows at
    /// fixed time produced two different digests. The run references now live in
    /// <see cref="EpisodeBytes"/>, where varying is what they are for.
    /// </para>
    /// </remarks>
    public ReadOnlyMemory<byte> DerivationBytes { get; }

    /// <summary>The digest of <see cref="DerivationBytes"/>. Two identical executions agree.</summary>
    public string DerivationSha256 { get; }

    /// <summary>
    /// WHICH EXECUTION OBSERVED IT, canonicalized: every run-specific reference the proofs carry,
    /// bound to the derivation by its digest.
    /// </summary>
    /// <remarks>
    /// Retained beside the derivation rather than dropped. Two runs over identical rows produce one
    /// derivation artifact and two episode records, which is the accurate shape: what the publisher
    /// said is one fact, and each time it was asked is another.
    /// </remarks>
    public ReadOnlyMemory<byte> EpisodeBytes { get; }

    /// <summary>The digest of <see cref="EpisodeBytes"/>. Two executions differ, by design.</summary>
    public string EpisodeSha256 { get; }

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

        var derivationBytes = ContractCanonicalizer.Canonicalize(
            new CanonicalDerivationDocument(
                DerivationSchema,
                CanonicalProofDocument.Of(expressionFacts.Proof),
                objectFacts is null ? null : CanonicalProofDocument.Of(objectFacts.Proof),
                [.. appended.Select(CanonicalExpressionDocument.Of)]),
            DerivationSchema + "-canonical-json",
            64);

        var episodeBytes = ContractCanonicalizer.Canonicalize(
            new CanonicalEpisodeDocument(
                EpisodeSchema,
                Convert.ToHexStringLower(SHA256.HashData(derivationBytes)),
                CanonicalEpisodeProofDocument.Of(expressionFacts.Proof),
                objectFacts is null ? null : CanonicalEpisodeProofDocument.Of(objectFacts.Proof)),
            EpisodeSchema + "-canonical-json",
            64);

        return new EuLanguageScopedExpressionDerivation(
            expressionFacts.Proof,
            objectFacts?.Proof,
            appended,
            derivationBytes,
            episodeBytes);
    }

    private sealed record CanonicalDerivationDocument(
        string Schema,
        CanonicalProofDocument ExpressionFactsProof,
        CanonicalProofDocument? ObjectFactsProof,
        IReadOnlyList<CanonicalExpressionDocument> Expressions);

    private sealed record CanonicalEpisodeDocument(
        string Schema,
        string DerivationSha256,
        CanonicalEpisodeProofDocument ExpressionFactsProof,
        CanonicalEpisodeProofDocument? ObjectFactsProof);

    /// <summary>
    /// The half of a proof that identifies WHAT WAS OBSERVED rather than which run observed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY FIELD OF EVERY PROOF STILL TRAVELS - across the two documents, not out of them. #584's
    /// review found the opposite defect one type over: a comparison read four of a proof's seven
    /// fields, so a proof retained under the weaker custody class replayed as identical to a floored
    /// one. <c>RetainedFloor</c> is therefore here, in the identity, where a weaker custody class
    /// makes a different derivation.
    /// </para>
    /// <para>
    /// The three references that are NOT here are in
    /// <see cref="CanonicalEpisodeProofDocument"/>, and the reason is measurable rather than
    /// stylistic: all three are minted per run. <c>AcquisitionRunRef</c> comes from a fresh
    /// <c>Guid.NewGuid()</c> per session; the interpretation profile reference is minted with a new
    /// URN per run. A derivation identity containing them cannot be stable, which is what S3-A04
    /// requires it to be.
    /// </para>
    /// </remarks>
    private sealed record CanonicalProofDocument(
        string FamilyKey,
        long DeliveredRowCount,
        string CanonicalKeyDigest,
        string RetainedFloor)
    {
        public static CanonicalProofDocument Of(AbsenceFamilyEnumerationProof proof) =>
            new(
                proof.FamilyKey,
                proof.DeliveredRowCount,
                proof.CanonicalKeyDigest,
                proof.RetainedFloor.ToString());
    }

    /// <summary>The half of a proof that names the execution. Varies between runs, by design.</summary>
    private sealed record CanonicalEpisodeProofDocument(
        string FamilyKey,
        string AcquisitionRunResourceId,
        string AcquisitionRunSha256,
        string InterpretationProfileResourceId,
        string InterpretationProfileSha256,
        string SourceProfileResourceId,
        string SourceProfileSha256)
    {
        public static CanonicalEpisodeProofDocument Of(AbsenceFamilyEnumerationProof proof) =>
            new(
                proof.FamilyKey,
                proof.AcquisitionRunRef.ResourceId,
                proof.AcquisitionRunRef.Sha256,
                proof.InterpretationProfileRef.ResourceId,
                proof.InterpretationProfileRef.Sha256,
                proof.SourceProfileRef.ResourceId,
                proof.SourceProfileRef.Sha256);
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
