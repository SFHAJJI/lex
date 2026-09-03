using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The construction surface of the refrozen R8 EU legal-notice evidence type.
///
/// <para>
/// Pinned per the refreeze objection (coordination/EVENTS.md event
/// lex-event-20260903T173221003Z-887bf79258394fe8a8791f77effa758e): "pin the door through
/// ConstructionSurface.Of". <see cref="EuLegalNoticeEvidence.FromRoute"/> is the only path that
/// mints a new instance from a real observation: it takes a real <see cref="RoutedHttpEvidence"/>,
/// already proven by <see cref="RoutedHttpEvidence.Create"/>'s Decision 80 receipt gate, together
/// with the <see cref="HttpLogicalRequest"/> that produced it. If a second producer of
/// <see cref="EuLegalNoticeEvidence"/> ever appears anywhere in this assembly, it can describe bytes
/// nothing actually retained, or a route that never actually happened, and this pin is where that
/// shows up.
/// </para>
/// <para>
/// Unlike <c>RepresentationChainObservation</c> (item 9's own two-producer precedent), this type
/// also carries <see cref="EuLegalNoticeEvidence.ParseAndVerify"/>: R8 evidence must be storable and
/// referenced by digest through <see cref="EuLegalNoticeEvidence.ToArtifactRef"/>, so it needs a
/// canonical-JSON round trip that <c>RepresentationChainObservation</c>, a pure in-memory value, does
/// not. <see cref="EuLegalNoticeEvidence.ParseAndVerify"/> is deliberately visible here as a third
/// producer rather than hidden, exactly like <c>RoutedHttpEvidence.ParseAndVerify</c> is pinned
/// alongside its own <c>Create</c> in <c>RoutedHttpEvidenceSurfaceTests</c>.
/// </para>
/// </summary>
[TestClass]
public sealed class EuLegalNoticeEvidenceConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence";
    private const string Http = "Lex.V3.Contracts.Source.Http.";

    [TestMethod]
    public void EvidenceIsMintedByExactlyTwoDeclaredPathsPlusItsPrivateConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "::.ctor("
                + "System.String, System.String, " + Http + "RoutedHttpSingleHeader, "
                + Http + "RoutedHttpHeaderField, " + Http + "RoutedHttpHeaderField, "
                + Http + "RoutedHttpHeaderField, System.UInt64, System.String, System.String, "
                + "System.String) -> " + N,
                // The receipt-checked production door: takes a real RoutedHttpEvidence (already
                // proven by RoutedHttpEvidence.Create's Decision 80 receipt gate) and the exact
                // HttpLogicalRequest that produced it. Refuses a non-GET, a request that is not the
                // one the terminal hop actually sent, a route that did not start at the pinned R8
                // URI, a route whose terminal hop does not share the pinned URI's own host and port,
                // a non-200 terminal status, and anything but a single text/html media type.
                "method public static " + N + "::FromRoute("
                + Http + "RoutedHttpEvidence, " + Http + "HttpLogicalRequest) -> " + N,
                // Not a second unguarded production door: it re-derives an already-canonical value
                // from its own wire bytes and proves only that the bytes are that value's exact
                // canonical form (round trip), the same relationship RoutedHttpEvidence.ParseAndVerify
                // has to RoutedHttpEvidence.Create. It cannot and does not re-demand a receipt the
                // canonical JSON never carries.
                "method public static " + N + "::ParseAndVerify(System.ReadOnlySpan<System.Byte>) -> " + N,
            },
            ConstructionSurface.Of(typeof(EuLegalNoticeEvidence)).ToArray(),
            "a new path onto legal-notice evidence must be justified in review, not discovered later");
    }

    [TestMethod]
    public void NoOtherTypeInContractsHoldsOrProducesLegalNoticeEvidence()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuLegalNoticeEvidence).Assembly, typeof(EuLegalNoticeEvidence), true).ToArray(),
            "legal-notice evidence reached a new holder in Contracts; EuRightsDisposition and "
            + "EuRightsExceptionDisposition consume only the SourceArtifactRef ToArtifactRef "
            + "produces, never the evidence object itself");
    }
}
