using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Derivation;

/// <summary>Why an expression could not be appended to a set that already holds its identity.</summary>
/// <remarks>
/// Closed at two members because an append has exactly two failing shapes to distinguish and one
/// succeeding one. Re-presenting an identity whose retained bytes are the same is not a failure at
/// all - it is the idempotence an append-only store must have to survive a replay - so it is
/// <see cref="None"/> rather than a refusal, and the set's count does not grow.
/// </remarks>
public enum LanguageScopedExpressionAppendRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The same expression identity was presented with different retained bytes.</summary>
    [JsonStringEnumMemberName("conflicting_canonical_bytes")]
    ConflictingCanonicalBytes = 1,
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
    private LanguageScopedExpression(
        LanguageScopedExpressionIdentity identity,
        string officialLanguage,
        PublisherCorrigendumDate? publisherCorrigendumDate,
        SourceObjectRef sourceObject,
        DurableBlobWriteReceipt retainedTransportBytes)
    {
        Identity = identity;
        OfficialLanguage = officialLanguage;
        PublisherCorrigendumDate = publisherCorrigendumDate;
        SourceObject = sourceObject;
        RetainedTransportBytes = retainedTransportBytes;
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

    /// <summary>The custody receipt for the exact transport bytes this expression came from.</summary>
    public DurableBlobWriteReceipt RetainedTransportBytes { get; }

    /// <summary>
    /// The content address of the retained bytes: what two presentations of one identity are
    /// compared on.
    /// </summary>
    public string CanonicalBytesSha256 => RetainedTransportBytes.Reference.ContentSha256;

    /// <summary>
    /// The only door. Every argument is either validated here or carried from a verified boundary.
    /// </summary>
    public static LanguageScopedExpression FromRetainedSource(
        LanguageScopedExpressionIdentity identity,
        string officialLanguage,
        PublisherCorrigendumDate? publisherCorrigendumDate,
        SourceObjectRef sourceObject,
        DurableBlobWriteReceipt retainedTransportBytes)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(retainedTransportBytes);

        return new LanguageScopedExpression(
            identity,
            ContractValidation.RequireIdentifier(officialLanguage, nameof(officialLanguage)),
            publisherCorrigendumDate,
            sourceObject,
            retainedTransportBytes);
    }
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
/// IDEMPOTENT ON IDENTICAL BYTES, REFUSING ON CONFLICT. Re-presenting an identity whose retained
/// bytes match is accepted and changes nothing, so a replayed run converges instead of growing.
/// Re-presenting the same identity with different retained bytes is the one thing an append-only
/// store must not silently absorb, and it refuses by name.
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
    private readonly Dictionary<LanguageScopedExpressionIdentity, string> _bytesByIdentity = [];

    /// <summary>Every admitted expression, in the order it was admitted.</summary>
    public IReadOnlyList<LanguageScopedExpression> Expressions => _expressions;

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

        if (_bytesByIdentity.TryGetValue(expression.Identity, out var heldBytes))
        {
            if (!string.Equals(heldBytes, expression.CanonicalBytesSha256, StringComparison.Ordinal))
            {
                refusal = LanguageScopedExpressionAppendRefusal.ConflictingCanonicalBytes;
                return false;
            }

            refusal = LanguageScopedExpressionAppendRefusal.None;
            return true;
        }

        _bytesByIdentity.Add(expression.Identity, expression.CanonicalBytesSha256);
        _expressions.Add(expression);
        refusal = LanguageScopedExpressionAppendRefusal.None;
        return true;
    }
}
