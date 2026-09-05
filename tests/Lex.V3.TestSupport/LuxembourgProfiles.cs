using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.TestSupport;

/// <summary>
/// Opens a Luxembourg vocabulary snapshot that a fixture intends to be valid, and fails loudly
/// naming the typed refusal when it is not.
/// </summary>
/// <remarks>
/// <para>
/// Residue R1 replaced <c>VerifiedLuxembourgSourceProfile.Open</c>, which threw an untyped
/// <see cref="System.ArgumentException"/> for two conditions the failure vocabulary already named,
/// with <c>TryOpen</c> and a typed whole-run failure. Most call sites were never testing those
/// conditions; they build a complete snapshot and expect a profile. This is their door, so those
/// sites say what they mean and a fixture that silently stopped being complete fails with the code
/// and subject rather than with a null reference somewhere later.
/// </para>
/// <para>
/// The throw here is a test-fixture assertion and not a second production path. THIS DOOR IS THE
/// ONLY CALLER TODAY: no src caller exists, because the LU composition root is Stage 6. The R6-01
/// root will be the first production caller, and it maps a failure to
/// <c>LuxembourgQueryExecutionRefusal.ScopeResolutionFailed</c> with its detail, a member that
/// already exists. A test that means to observe a refusal calls <c>TryOpen</c> directly, because a
/// helper that hides the failure could not observe it.
/// </para>
/// </remarks>
public static class LuxembourgProfiles
{
    public static VerifiedLuxembourgSourceProfile Opened(LuxembourgVocabularySnapshot snapshot)
    {
        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);
        return profile ?? throw new InvalidOperationException(
            "A fixture snapshot did not open: " + failure!.Code + " on " + failure.Subject);
    }
}
