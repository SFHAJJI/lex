using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class LuxembourgDocumentFetchAddressTests
{
    private const string RealExampleStoreUri =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
        + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml";

    [TestMethod]
    public void AValidStoreUriIsAccepted()
    {
        var fileUri = LuxembourgFileUri.RequireValid(RealExampleStoreUri);

        Assert.AreEqual(RealExampleStoreUri, fileUri.Value.AbsoluteUri);
    }

    [TestMethod]
    public void AHttpsStoreUriIsAlsoAccepted()
    {
        var httpsUri = RealExampleStoreUri.Replace("http://", "https://", StringComparison.Ordinal);
        var fileUri = LuxembourgFileUri.RequireValid(httpsUri);

        Assert.AreEqual(httpsUri, fileUri.Value.AbsoluteUri);
    }

    [TestMethod]
    public void TheRealExampleMapsToTheExactWwwHostFetchUri()
    {
        // Item 3, proven on the real live-verified example from the scope ruling: same path, host
        // changed (drop "data."), scheme forced to https regardless of the store URI's own scheme.
        var fileUri = LuxembourgFileUri.RequireValid(RealExampleStoreUri);

        var fetchUri = fileUri.ToFetchUri();

        Assert.AreEqual(
            "https://legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/"
            + "eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml",
            fetchUri.AbsoluteUri);
    }

    [TestMethod]
    public void AnHttpsStoreUriAlsoNormalizesToHttpsOnTheWwwHost()
    {
        var httpsUri = RealExampleStoreUri.Replace("http://", "https://", StringComparison.Ordinal);
        var fileUri = LuxembourgFileUri.RequireValid(httpsUri);

        var fetchUri = fileUri.ToFetchUri();

        Assert.AreEqual(Uri.UriSchemeHttps, fetchUri.Scheme);
        Assert.AreEqual("legilux.public.lu", fetchUri.Host);
        Assert.IsTrue(fetchUri.IsDefaultPort);
    }

    [TestMethod]
    public void ANonAbsoluteUriIsRefusedAndNamesTheRejectedUri()
    {
        const string candidate = "not-a-uri-at-all";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.NotAbsoluteUri, refusal.Reason);
        StringAssert.Contains(refusal.Message, candidate);
    }

    [TestMethod]
    public void AnUnsupportedSchemeIsRefused()
    {
        const string candidate = "ftp://data.legilux.public.lu/filestore/x.xml";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.UnsupportedScheme, refusal.Reason);
    }

    [TestMethod]
    public void TheWrongHostIsRefusedAndNamed()
    {
        // Including the already-mapped www host itself: the store validator only ever admits the
        // data host, exactly like v2.
        const string candidate = "https://legilux.public.lu/filestore/x.xml";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.UnexpectedHost, refusal.Reason);
        StringAssert.Contains(refusal.Message, candidate);
    }

    [TestMethod]
    public void ANonDefaultPortIsRefused()
    {
        const string candidate = "https://data.legilux.public.lu:8443/filestore/x.xml";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.NonDefaultPort, refusal.Reason);
    }

    [TestMethod]
    public void UserInfoIsRefused()
    {
        const string candidate = "https://user:pass@data.legilux.public.lu/filestore/x.xml";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(LuxembourgFileUriRefusalReason.UserInfoPresent, refusal.Reason);
    }

    [TestMethod]
    public void AQueryStringIsRefused()
    {
        const string candidate = "https://data.legilux.public.lu/filestore/x.xml?download=1";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.QueryPresent, refusal.Reason);
    }

    [TestMethod]
    public void AFragmentIsRefused()
    {
        const string candidate = "https://data.legilux.public.lu/filestore/x.xml#section";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.FragmentPresent, refusal.Reason);
    }

    [TestMethod]
    public void APathNotUnderFilestoreIsRefused()
    {
        const string candidate = "https://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.PathNotUnderFilestore, refusal.Reason);
    }

    [TestMethod]
    public void APathExactlyEqualToTheFilestorePrefixIsRefused()
    {
        // The ruling's own words: "strictly longer than that prefix" - the prefix alone, with
        // nothing after it, must be refused, not silently accepted as "under the prefix".
        const string candidate = "https://data.legilux.public.lu/filestore/";

        var refusal = Assert.ThrowsExactly<LuxembourgFileUriRefusedException>(() =>
            LuxembourgFileUri.RequireValid(candidate));

        Assert.AreEqual(candidate, refusal.RejectedUri);
        Assert.AreEqual(LuxembourgFileUriRefusalReason.PathNotUnderFilestore, refusal.Reason);
    }
}
