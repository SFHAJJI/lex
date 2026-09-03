using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a Formex package carries no body reference. Closed, and every member is a refusal: a
/// package that cannot say which item is the legal text has no body, rather than a probable one.
/// </summary>
public enum EuFormexPackageRefusal
{
    /// <summary>No refusal: the package was admitted and carries a body reference.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The manifestation is not one of this expression's own. Named for what is compared: the
    /// manifestation's parent key against the expression's key. It is not a statement about the
    /// CELEX, which no key in this type carries.
    /// </summary>
    [JsonStringEnumMemberName("expression_disagreement")]
    ExpressionDisagreement = 1,

    /// <summary>
    /// The item set names a language other than the one this package was opened for. Language is
    /// expression level at this publisher: a different language is a different manifestation, not
    /// a filtered view of this one.
    /// </summary>
    [JsonStringEnumMemberName("language_disagreement")]
    LanguageDisagreement = 2,

    /// <summary>
    /// The items were not observed under the manifestation this package names, so the set
    /// describes some other package that happens to agree about work and language.
    /// </summary>
    [JsonStringEnumMemberName("manifestation_disagreement")]
    ManifestationDisagreement = 3,
}

/// <summary>
/// One Cellar manifestation of one consolidated act in one language, joined to the classified
/// Formex items observed under it.
/// </summary>
/// <remarks>
/// <para>
/// This is the join that makes an item classification load-bearing. <see cref="EuFormexItemSet"/>
/// decides which item is the legal text and which is the descriptor; this type decides whether that
/// set actually belongs to the manifestation and expression a caller is holding, and only then does
/// a body reference exist. The two questions are separate because the answers come from different
/// authorities: the grammar answers the first, the identity boundary answers the second.
/// </para>
/// <para>
/// <see cref="BodyRef"/> is the only public body reference on the Union side and it is present by
/// construction. There is no admitted package without a main text, because
/// <see cref="EuFormexItemSet"/> refuses a set that has none, and there is no way to reach this
/// type except through a set that was admitted. A caller therefore cannot hold a package and ask
/// whether it has a body: if it exists, it does.
/// </para>
/// <para>
/// What it deliberately does not decide: whether the bytes were acquired, whether the acquisition
/// was permitted, whether the reuse basis allows publication, and whether this consolidation is the
/// one in force on any date. A package is a statement about which file is the law, not about
/// whether we may hold it or when it applied.
/// </para>
/// </remarks>
public sealed class EuFormexPackage
{
    private EuFormexPackage(
        SourceObjectRef manifestationRef,
        SourceObjectRef expressionRef,
        EuFormexItemSet items,
        string workCelex,
        string language)
    {
        ManifestationRef = manifestationRef;
        ExpressionRef = expressionRef;
        Items = items;
        WorkCelex = workCelex;
        Language = language;
    }

    /// <summary>The Cellar manifestation these items were observed under.</summary>
    public SourceObjectRef ManifestationRef { get; }

    /// <summary>The expression that manifestation belongs to, proved by the boundary.</summary>
    public SourceObjectRef ExpressionRef { get; }

    /// <summary>The classified items, main text present by construction.</summary>
    public EuFormexItemSet Items { get; }

    /// <summary>
    /// The base act CELEX every item agreed with, carried verbatim from the item set.
    /// </summary>
    /// <remarks>
    /// Carried, not proven here. Nothing in this type ties a CELEX to the Cellar work UUID,
    /// because that link lives in the notice rather than in any key, so no member of this type is
    /// evidence that this package belongs to that CELEX beyond the items agreeing among themselves.
    /// </remarks>
    public string WorkCelex { get; }

    /// <summary>The two letter expression language.</summary>
    public string Language { get; }

    /// <summary>
    /// The item carrying legal text. The only body reference the Union side publishes, and present
    /// whenever a package exists.
    /// </summary>
    public SourceObjectRef BodyRef => Items.MainText.ItemRef;

    /// <summary>The paired descriptor when the publisher served one. Never a body.</summary>
    public SourceObjectRef? DescriptorRef => Items.Descriptor?.ItemRef;

    /// <summary>
    /// The only path that mints a package.
    /// </summary>
    /// <param name="boundary">Admits the manifestation and expression as exact Cellar roles.</param>
    /// <param name="manifestationRef">The manifestation the items were observed under.</param>
    /// <param name="expressionRef">Its parent expression.</param>
    /// <param name="items">An already admitted item set.</param>
    /// <param name="expectedLanguage">The language this package is opened for.</param>
    /// <param name="refusal">Why no package exists, when none does.</param>
    public static EuFormexPackage? TryAdmit(
        EuWemiIdentityBoundary boundary,
        SourceObjectRef manifestationRef,
        SourceObjectRef expressionRef,
        EuFormexItemSet items,
        string expectedLanguage,
        out EuFormexPackageRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrEmpty(expectedLanguage);

        var manifestation = boundary.Require(
            manifestationRef, EuWemiRole.Manifestation, nameof(manifestationRef));
        var expression = boundary.Require(
            expressionRef, EuWemiRole.Expression, nameof(expressionRef));

        refusal = EuFormexPackageRefusal.None;

        if (!string.Equals(items.Language, expectedLanguage, StringComparison.Ordinal))
        {
            refusal = EuFormexPackageRefusal.LanguageDisagreement;
            return null;
        }

        // The manifestation must be the one the boundary attached to this expression, not merely
        // another manifestation of the same work. The boundary proves the parent chain; comparing
        // the keys here is what ties this package's two references to each other rather than each
        // to the registry separately.
        // ParentKeyRef is non-null here: boundary.Require above throws on a manifestation with no
        // parent, so a null guard would be unreachable and read as coverage.
        if (!string.Equals(manifestation.ParentKeyRef!.CanonicalKey, expression.CanonicalKey,
                StringComparison.Ordinal))
        {
            refusal = EuFormexPackageRefusal.ExpressionDisagreement;
            return null;
        }

        // Every item must have been observed under this exact manifestation. Without this an item
        // set from another package that agrees about work and language would attach here, and
        // agreement about work and language is exactly what a sibling package has.
        foreach (var item in items.Items)
        {
            // Likewise non-null: EuFormexItem's constructor admitted this ref through the same
            // boundary with the Item role. Unreachable by agreement between the two types, and a
            // tripwire needs a mutation that can reach it, which none can.
            if (!string.Equals(item.ItemRef.ParentKeyRef!.CanonicalKey, manifestation.CanonicalKey,
                    StringComparison.Ordinal))
            {
                refusal = EuFormexPackageRefusal.ManifestationDisagreement;
                return null;
            }
        }

        return new EuFormexPackage(
            manifestation, expression, items, items.WorkCelex, items.Language);
    }
}
