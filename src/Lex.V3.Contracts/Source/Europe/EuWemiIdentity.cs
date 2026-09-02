using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The four Cellar object roles, closed.
/// </summary>
/// <remarks>
/// <para>
/// These are WEMI roles carried as source-object coordinates, not attributes. A CELEX, a language
/// and a format describe a thing; they do not say which of the four it is, and none of them can
/// stand in for the role. That distinction is the whole reason this type exists: an earlier slice
/// proved an expression belonged to a work by comparing a parent key to a CELEX, which set an
/// attribute equal to an identity and proved only that somebody wrote the same string twice.
/// </para>
/// <para>
/// The publisher's grammar carries the role, per the verified record: a work is a bare UUID, an
/// expression is <c>{work}.{four digits}</c>, a manifestation is <c>{expression}.{two digits}</c>,
/// and an item is the data stream beneath a manifestation. Each of the first three was confirmed
/// live against the official endpoint, resolving 200 to its own distinct <c>rdf/object/full</c>.
/// </para>
/// </remarks>
public enum EuWemiRole
{
    [JsonStringEnumMemberName("eu_cellar_work")]
    Work = 1,

    [JsonStringEnumMemberName("eu_cellar_expression")]
    Expression = 2,

    [JsonStringEnumMemberName("eu_cellar_manifestation")]
    Manifestation = 3,

    [JsonStringEnumMemberName("eu_cellar_item")]
    Item = 4,
}

/// <summary>
/// The one place a Union source object is admitted as a Cellar object of a stated role.
/// </summary>
/// <remarks>
/// <para>
/// Constructed once with the exact registry and identity-profile references this scope trusts, then
/// asked to admit objects. The references are supplied rather than hard-coded because they are
/// content-addressed facts about a deployed registry, and a constant here would be this contract
/// asserting something only the registry can say.
/// </para>
/// <para>
/// Every check below exists because its absence was reachable. <see cref="SourceObjectRef"/>
/// guarantees only that a child and its parent share <em>a</em> registry, so matching the member-key
/// string alone admits a same-named member from any registry and any identity profile. Matching the
/// role without the grammar admits a work-shaped key labelled as an expression. Matching the grammar
/// without the parent chain admits an expression of one work attached to another. Matching the
/// parent chain without the UUID prefix admits a parent that is a real work and simply not this
/// one.
/// </para>
/// <para>
/// It decides nothing else. Whether the object was acquired, whether its family is complete, and
/// whether an absence may be asserted are all questions this boundary deliberately cannot answer.
/// </para>
/// </remarks>
public sealed class EuWemiIdentityBoundary
{
    private readonly SourceArtifactRef _registryRef;
    private readonly SourceArtifactRef _identityProfileRef;

    public EuWemiIdentityBoundary(
        SourceArtifactRef registryRef,
        SourceArtifactRef identityProfileRef)
    {
        _registryRef = registryRef ?? throw new ArgumentNullException(nameof(registryRef));
        _identityProfileRef = identityProfileRef
            ?? throw new ArgumentNullException(nameof(identityProfileRef));
    }

    /// <summary>The exact member key a role must carry, as the registry spells it.</summary>
    public static string MemberKeyOf(EuWemiRole role) =>
        ContractValidation.RequireDefined(role, nameof(role)) switch
        {
            EuWemiRole.Work => "eu_cellar_work",
            EuWemiRole.Expression => "eu_cellar_expression",
            EuWemiRole.Manifestation => "eu_cellar_manifestation",
            _ => "eu_cellar_item",
        };

    /// <summary>The role a given role's parent must carry, or null where the role is a root.</summary>
    public static EuWemiRole? ParentRoleOf(EuWemiRole role) =>
        ContractValidation.RequireDefined(role, nameof(role)) switch
        {
            EuWemiRole.Work => null,
            EuWemiRole.Expression => EuWemiRole.Work,
            EuWemiRole.Manifestation => EuWemiRole.Expression,
            _ => EuWemiRole.Manifestation,
        };

    /// <summary>
    /// Admit an object as a Cellar object of this exact role, or refuse it.
    /// </summary>
    public SourceObjectRef Require(SourceObjectRef value, EuWemiRole role, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        ContractValidation.RequireDefined(role, nameof(role));

        if (value.Authority != SourceAuthority.Cellar)
        {
            throw new ArgumentException(
                $"A Cellar object must carry the Cellar authority, not {value.Authority}.",
                parameterName);
        }

        RequireRegistryMember(value.EntityKind, role, parameterName);

        if (value.IdentityProfileRef != _identityProfileRef)
        {
            throw new ArgumentException(
                "The object was minted against a different identity profile.", parameterName);
        }

        if (!MatchesGrammar(value.CanonicalKey, role))
        {
            throw new ArgumentException(
                $"The canonical key does not match the Cellar grammar for {role}.", parameterName);
        }

        var parentRole = ParentRoleOf(role);
        if (parentRole is null)
        {
            if (value.ParentKeyRef is not null)
            {
                throw new ArgumentException(
                    "A Cellar work is a root and cannot name a parent.", parameterName);
            }

            return value;
        }

        if (value.ParentKeyRef is not { } parent)
        {
            throw new ArgumentException(
                $"A Cellar {role} must name its parent {parentRole}.", parameterName);
        }

        RequireRegistryMember(parent.EntityKind, parentRole.Value, parameterName);

        if (!MatchesGrammar(parent.CanonicalKey, parentRole.Value))
        {
            throw new ArgumentException(
                $"The parent key does not match the Cellar grammar for {parentRole}.",
                parameterName);
        }

        // The parent must be this object's own ancestor, not merely a well-formed one of the right
        // role. Without this a real expression of another work is admitted, and it is the hardest
        // case to see by reading because every other property of it is correct.
        //
        // A prefix test is enough on its own. An earlier version also refused an equal-length
        // parent, which reads as prudent and is unreachable: both keys have already passed their
        // own role's grammar, and no work key and expression key can have the same length. A
        // mutation removing that clause survived, which is what dead defensive code looks like.
        if (!value.CanonicalKey.StartsWith(parent.CanonicalKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {role} key {value.CanonicalKey} does not descend from {parent.CanonicalKey}.",
                parameterName);
        }

        return value;
    }

    private void RequireRegistryMember(
        SourceRegistryMemberRef member,
        EuWemiRole role,
        string parameterName)
    {
        if (member.RegistryRef != _registryRef)
        {
            throw new ArgumentException(
                "The entity kind comes from a different registry.", parameterName);
        }

        var expected = MemberKeyOf(role);
        if (!string.Equals(member.MemberKey, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The entity kind is {member.MemberKey}, not {expected}.", parameterName);
        }
    }

    /// <summary>
    /// Whether a canonical key has the publisher's shape for a role.
    /// </summary>
    /// <remarks>
    /// Written against the grammar rather than against a regex so each level is separately
    /// readable, and so a wrong suffix depth is refused by the level it is not rather than by
    /// accident. The digit widths are the publisher's, not ours: four for an expression, two for a
    /// manifestation.
    /// </remarks>
    private static bool MatchesGrammar(string key, EuWemiRole role)
    {
        if (key is null || key.Length == 0)
        {
            return false;
        }

        var stream = key.IndexOf('/', StringComparison.Ordinal);
        var head = stream < 0 ? key : key[..stream];
        var tail = stream < 0 ? null : key[(stream + 1)..];

        if (role == EuWemiRole.Item)
        {
            // The whitelist carries the whole rule, including the nested-path case: a second
            // slash is not an admitted character, so it is refused by the same test that refuses
            // any other. An explicit contains-slash check here survived its mutation because
            // nothing could reach it.
            return tail is { Length: > 0 } &&
                tail.All(static c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_') &&
                MatchesGrammar(head, EuWemiRole.Manifestation);
        }

        if (stream >= 0)
        {
            return false;
        }

        var parts = head.Split('.');
        var expected = role switch
        {
            EuWemiRole.Work => 1,
            EuWemiRole.Expression => 2,
            _ => 3,
        };

        if (parts.Length != expected || !Guid.TryParseExact(parts[0], "D", out _))
        {
            return false;
        }

        return (parts.Length < 2 || IsDigits(parts[1], 4)) &&
            (parts.Length < 3 || IsDigits(parts[2], 2));
    }

    private static bool IsDigits(string value, int width) =>
        value.Length == width && value.All(static c => c is >= '0' and <= '9');
}
