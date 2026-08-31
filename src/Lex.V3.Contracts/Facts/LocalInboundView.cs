using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A locally computed inbound view: "these held records point at this target".
/// </summary>
/// <remarks>
/// <para>
/// This carries no publisher authority. It is not an inverse and must never be rendered as one:
/// the publisher has not said that the target relates back to anything. All this states is that,
/// within the scope actually observed, these edges were seen pointing inward.
/// </para>
/// <para>
/// <see cref="ScopeIsComplete"/> is required and unforgiving on purpose. An inbound view computed
/// over a partial corpus is a different claim from one computed over a complete scope, and a
/// reader that cannot tell them apart will read absence as evidence. Every contributing edge is
/// named so the view can be recomputed and checked rather than trusted.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalInboundView
{
    public const string Identity = FactsSchemaIds.LocalInboundView;

    [JsonConstructor]
    public LocalInboundView(
        string schema,
        OfficialIdentity target,
        string predicateUri,
        bool scopeIsComplete,
        string scopeDescriptorSha256,
        IReadOnlyList<PublisherRelation> contributingAssertions)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The local inbound view schema must be version 1.", nameof(schema));
        }

        if (!FactsValidation.IsAbsoluteUri(predicateUri))
        {
            throw new ArgumentException(
                "An inbound view predicate must be an absolute URI.",
                nameof(predicateUri));
        }

        if (!FactsValidation.IsLowercaseSha256(scopeDescriptorSha256))
        {
            throw new ArgumentException(
                "An inbound view must bind the digest of the scope it was computed over.",
                nameof(scopeDescriptorSha256));
        }

        ArgumentNullException.ThrowIfNull(contributingAssertions);
        var contributing = contributingAssertions.ToArray();
        if (Array.IndexOf(contributing, null) >= 0)
        {
            throw new ArgumentException(
                "A contributing assertion cannot be null.",
                nameof(contributingAssertions));
        }

        foreach (var assertion in contributing)
        {
            if (!string.Equals(assertion.PredicateUri, predicateUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every contributing assertion must carry the predicate of the view.",
                    nameof(contributingAssertions));
            }
        }

        Schema = schema;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        PredicateUri = predicateUri;
        ScopeIsComplete = scopeIsComplete;
        ScopeDescriptorSha256 = scopeDescriptorSha256;
        ContributingAssertions = Array.AsReadOnly(contributing);
    }

    public string Schema { get; }

    public OfficialIdentity Target { get; }

    public string PredicateUri { get; }

    /// <summary>
    /// Whether the scope this view was computed over is complete. False means absence from this
    /// view proves nothing at all.
    /// </summary>
    public bool ScopeIsComplete { get; }

    public string ScopeDescriptorSha256 { get; }

    public IReadOnlyList<PublisherRelation> ContributingAssertions { get; }
}
