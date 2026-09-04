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
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(200, retryAllowanceSpent: false);

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
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(404, retryAllowanceSpent: false);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.NotFound, outcome.Kind);
        Assert.AreEqual(404, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "404");
    }

    [TestMethod]
    public void Http410ClassifiesAsGone()
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(410, retryAllowanceSpent: false);

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
    public void RetryableStatusesClassifyAsRetryExhaustedOnceTheAllowanceIsSpent(int status)
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(
            status, retryAllowanceSpent: true);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.RetryExhausted, outcome.Kind);
        Assert.AreEqual(status, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, status.ToString());
    }

    /// <summary>
    /// D1-06c-LU-2: the name has to be earned. Before this, the six retryable statuses classified
    /// as RetryExhausted on the FIRST observation of one, which named a retry policy that had not
    /// run. Classifying one of them while attempts remain is now refused outright rather than
    /// quietly downgraded to some other member, so no caller can produce the unearned claim by
    /// accident.
    /// </summary>
    [TestMethod]
    [DataRow(408)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public void ARetryableStatusCannotBeClassifiedTerminalWhileAttemptsRemain(int status)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            LuxembourgDocumentGetOutcome.FromObservedStatus(status, retryAllowanceSpent: false));

        StringAssert.Contains(exception.Message, "retry allowance");
    }

    /// <summary>
    /// The flag is not a general override: a status that is not retryable classifies the same way
    /// whichever value it is given, so nothing else in the ladder can be moved by passing true.
    /// </summary>
    [TestMethod]
    [DataRow(200)]
    [DataRow(404)]
    [DataRow(410)]
    [DataRow(451)]
    public void TheRetryAllowanceFlagChangesNothingForANonRetryableStatus(int status)
    {
        Assert.AreEqual(
            LuxembourgDocumentGetOutcome.FromObservedStatus(status, retryAllowanceSpent: false).Kind,
            LuxembourgDocumentGetOutcome.FromObservedStatus(status, retryAllowanceSpent: true).Kind);
    }

    [TestMethod]
    public void AnUnrecognisedStatusIsNamedRatherThanSilentlyWrapped()
    {
        var outcome = LuxembourgDocumentGetOutcome.FromObservedStatus(451, retryAllowanceSpent: false);

        Assert.AreEqual(LuxembourgDocumentGetOutcomeKind.UnexpectedPublisherStatus, outcome.Kind);
        Assert.AreEqual(451, outcome.ObservedStatus);
        StringAssert.Contains(outcome.Detail, "451");
    }

    [TestMethod]
    public void OutOfRangeStatusesAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuxembourgDocumentGetOutcome.FromObservedStatus(0, retryAllowanceSpent: false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LuxembourgDocumentGetOutcome.FromObservedStatus(600, retryAllowanceSpent: false));
    }
}
