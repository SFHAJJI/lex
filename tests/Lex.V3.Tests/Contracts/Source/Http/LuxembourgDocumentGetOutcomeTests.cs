using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class LuxembourgDocumentGetOutcomeTests
{
    [TestMethod]
    public void Http200ClassifiesAsRetrieved()
    {
        // Live-verified 2026-09-04: legilux.public.lu/filestore/.../xml/....xml returned HTTP 200,
        // Content-Type application/xml, genuine Akoma Ntoso (root <akomaNtoso>), 19,986 bytes,
        // SHA-256 9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d.
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(200);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.Retrieved, outcome.Kind);
        Assert.AreEqual(200, outcome.ObservedStatus);
    }

    [TestMethod]
    public void Http404ClassifiesAsNotFound()
    {
        // Live-verified 2026-09-04: a deliberately nonexistent filestore path returned HTTP 404,
        // Content-Type application/json, body {"timestamp":...,"status":404,"error":"Not Found",
        // "message":"No message available","path":...}, 234 bytes, SHA-256
        // efd7f3ff4dd45f9a9a303fad9353892c244154d940e24db8b1e480b7b8f4312c.
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(404);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.NotFound, outcome.Kind);
        Assert.AreEqual(404, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "404");
    }

    [TestMethod]
    public void Http410ClassifiesAsGone()
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(410);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.Gone, outcome.Kind);
        Assert.AreEqual(410, outcome.ObservedStatus);
    }

    [TestMethod]
    [DataRow(408)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public void RetryableStatusesClassifyAsRetryExhausted(int status)
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(status);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.RetryExhausted, outcome.Kind);
        Assert.AreEqual(status, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, status.ToString());
    }

    [TestMethod]
    public void AnUnrecognisedStatusIsNamedRatherThanSilentlyWrapped()
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(451);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.UnexpectedPublisherStatus, outcome.Kind);
        Assert.AreEqual(451, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "451");
    }

    [TestMethod]
    public void RobotsDisallowedCarriesNoWireStatusAndNamesTheRequestedPath()
    {
        // Item 4's per-object robots refusal: decided by the real live robots.txt this session
        // fetched (Disallow: /*.docx, /*.svg, /eli/etat/adm/, and named instances such as
        // /eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml), never a hardcoded path list here.
        const string path = "/eli/etat/leg/loi/2007/01/15/n2/jo/fr/xml";

        var outcome = LuxembourgDocumentGetOutcome.RobotsDisallowed(path);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.RobotsDisallowed, outcome.Kind);
        Assert.AreEqual(0, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, path);
    }

    [TestMethod]
    public void OutOfRangeStatusesAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuxembourgDocumentGetOutcome.FromObservedStatus(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuxembourgDocumentGetOutcome.FromObservedStatus(600));
    }
}
