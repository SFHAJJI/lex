using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Derivation;

/// <summary>The only two outcomes of comparing derivations by extraction profile.</summary>
public enum DerivationProfileComparisonOutcome
{
    [JsonStringEnumMemberName("comparable")]
    Comparable = 1,

    [JsonStringEnumMemberName("profiles_differ")]
    ProfilesDiffer = 2,
}

/// <summary>
/// Enforces the profile boundary before a caller can compare derived legal text.
/// </summary>
public static class DerivationProfileComparison
{
    public static DerivationProfileComparisonOutcome Compare(
        string leftProfileId,
        string rightProfileId)
    {
        ContractValidation.RequireIdentifier(leftProfileId, nameof(leftProfileId));
        ContractValidation.RequireIdentifier(rightProfileId, nameof(rightProfileId));

        return string.Equals(leftProfileId, rightProfileId, StringComparison.Ordinal)
            ? DerivationProfileComparisonOutcome.Comparable
            : DerivationProfileComparisonOutcome.ProfilesDiffer;
    }
}
