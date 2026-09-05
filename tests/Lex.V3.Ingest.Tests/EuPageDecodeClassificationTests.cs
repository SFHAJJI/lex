using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-05f part one: what the executor's page-parse classifier is allowed to blame.
/// </summary>
/// <remarks>
/// <para>
/// Every body here is a REAL RETAINED PUBLISHER RESPONSE from the EU canary's own custody store,
/// carried in as a fixture with its digest pinned, so an edited fixture fails on the digest before
/// it is ever decoded. They are the exact bytes the first two canary runs refused, and the four
/// page bodies were identical across two runs forty minutes apart, which is what made the failure
/// debuggable without touching the publisher again.
/// </para>
/// <para>
/// THE RULE: the default arm must never name the publisher.
/// <see cref="EuEnumerationRefusal.PageBodyMalformed"/> may only be produced for bytes that are
/// DEMONSTRABLY NOT what the interpretation profile promised, and the refusal must point at the
/// offending position. Everything else is
/// <see cref="EuEnumerationRefusal.PageDecodeFailed"/>, which names this executor.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuPageDecodeClassificationTests
{
    /// <summary>
    /// The object-facts page the canary refused: 39,498 bytes, 12 projected variables, 41 bindings
    /// against a count query that answered 41. Valid, complete SPARQL JSON. It is refused because
    /// key_4 is ABSENT from 8 of its 41 bindings, which is SPARQL 1.1's own encoding of an unbound
    /// term, and this executor's cursor extraction required every projected variable to be present.
    /// </summary>
    private const string ObjectFactsUnboundKey =
        "b0cd322be318bebe923bbe4ac97a10169f40e1ad40ec4e311e7a537a606d36c3";

    /// <summary>
    /// The publisher's own "Web Site Under Maintenance" page, 2,005 bytes of HTML, retained when a
    /// census family met a 503 mid-run. Used here as a page body because it is a real publisher
    /// response that is demonstrably not the SPARQL JSON the profile promises.
    /// </summary>
    private const string MaintenanceHtml =
        "e7fab335ce5367cfe359f9f7e0ad6ce1838bec9189a216bc3faf437ce169d404";

    [TestMethod]
    [DataRow("objectfacts-page-unbound-key.bin", ObjectFactsUnboundKey, 39498)]
    [DataRow("maintenance-page-not-json.bin", MaintenanceHtml, 2005)]
    [DataRow("expressionfacts-page.bin", "41a12ad8372e9a19065129c67c233e5cce5433d598fe191c2844946b064a4032", 1516)]
    [DataRow("rootwatermark-page.bin", "fb14660f2a0c881e48b06396611f1bfff8d632f7b18b7627458704ded30c248c", 1073)]
    [DataRow("manifestationfacts-page.bin", "2581d2c517b9405842ade20aef289f733556ba414a927983eb70862da7b9b6e9", 2625)]
    public void EveryRetainedPageFixtureIsTheExactBytesItsNameClaims(
        string fileName, string expectedSha256, int expectedLength)
    {
        var bytes = ReadFixture(fileName);

        Assert.HasCount(expectedLength, bytes);
        Assert.AreEqual(
            expectedSha256,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            $"{fileName} is not the retained publisher body its digest names.");
    }

    /// <summary>
    /// The object-facts body is VALID, COMPLETE SPARQL JSON, and the absent variable is exactly the
    /// unbound encoding. Asserted here so the classification test below cannot be read as agreeing
    /// that the bytes were bad.
    /// </summary>
    [TestMethod]
    public void TheRefusedObjectFactsPageIsValidSparqlJsonWithAnUnboundTerm()
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadFixture("objectfacts-page-unbound-key.bin"));
        var root = document.RootElement;
        var variables = root.GetProperty("head").GetProperty("vars")
            .EnumerateArray().Select(static value => value.GetString()!).ToArray();
        var bindings = root.GetProperty("results").GetProperty("bindings").EnumerateArray().ToArray();

        Assert.HasCount(12, variables);
        Assert.HasCount(41, bindings, "the count query for this partition answered 41.");

        var absentKey4 = bindings.Count(static binding => !binding.TryGetProperty("key_4", out _));
        var absentValue = bindings.Count(static binding => !binding.TryGetProperty("value", out _));
        Assert.AreEqual(8, absentKey4, "key_4 is absent from exactly the unbound rows.");
        Assert.AreEqual(
            absentValue,
            absentKey4,
            "key_4 mirrors value, so the two are absent together: that is one unbound term, not damage.");

        // And the rows that DO carry it are ordinary bound terms, so the body is not degenerate.
        Assert.AreEqual(33, bindings.Length - absentKey4);
    }

    /// <summary>
    /// A body that is not JSON at all IS demonstrably not what the profile promised, so it keeps
    /// <see cref="EuEnumerationRefusal.PageBodyMalformed"/>, and the refusal points at the position.
    /// </summary>
    [TestMethod]
    public void APageBodyThatIsNotJsonAtAllIsTheOneKindStillCalledMalformed()
    {
        var refusal = ClassifyPage("maintenance-page-not-json.bin");

        Assert.AreEqual(EuEnumerationRefusal.PageBodyMalformed, refusal.Code);
        StringAssert.Contains(
            refusal.CoreRefusalDetail,
            "not JSON at all",
            "the refusal must say what it observed rather than name a category.");
        StringAssert.Contains(refusal.CoreRefusalDetail, "line", "and point at the offending position.");
    }

    /// <summary>
    /// The canary's own refused page: OUR decode could not read it, so the refusal names US.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole of part one exists for. Before it, this body was reported as
    /// <see cref="EuEnumerationRefusal.PageBodyMalformed"/>, which said the publisher had sent bad
    /// bytes when the publisher had sent 41 correct bindings. The mislabel also HID the real cause,
    /// because a reader who trusts the name never opens the body.
    /// </remarks>
    [TestMethod]
    public void TheCanarysRefusedPageNowNamesOurDecodeAndCarriesItsOwnDigest()
    {
        var refusal = ClassifyPage("objectfacts-page-unbound-key.bin");

        Assert.AreEqual(
            EuEnumerationRefusal.PageDecodeFailed,
            refusal.Code,
            "valid SPARQL JSON this executor cannot read is OUR failure, never the publisher's.");
        StringAssert.Contains(refusal.CoreRefusalDetail, "FormatException", "the exception type travels with it.");
        StringAssert.Contains(refusal.CoreRefusalDetail, "key_4", "and the term the reader could not find.");
        StringAssert.Contains(
            refusal.CoreRefusalDetail,
            ObjectFactsUnboundKey,
            "and the page body's own digest, so the exact bytes can be reopened from the refusal alone.");
        Assert.AreEqual(ObjectFactsUnboundKey, refusal.ResponseBodySha256);
    }

    /// <summary>
    /// Drives one page body through the real executor and hands back the refusal it produced.
    /// </summary>
    private static EuEnumerationRefusalDetail ClassifyPage(string fileName)
    {
        var body = Encoding.UTF8.GetString(ReadFixture(fileName));
        var result = RunObjectFactsPageAsync(body).GetAwaiter().GetResult();

        Assert.IsNotNull(result.Refusal, $"{fileName} must refuse rather than deliver.");
        return result.Refusal!;
    }

    private static async Task<EuEnumerationRunResult> RunObjectFactsPageAsync(string pageBody)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder.Single(entry => entry.Celex == "32003L0088");
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        // A count of 41 and then the retained page, which is exactly the pair the live run met.
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["P"] = new EuAcquisitionTestFixture.FamilyScript(
                "P", [EuAcquisitionTestFixture.EuCountJson(41), pageBody]),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return await executor.RunObjectFactsPartitionAsync(
            new EuObjectFactsPartitionRunRequest(
                plan, planId, EuObjectFactsQuerySet.ObjectFacts, [rootIri],
                EuAcquisitionTestFixture.BuildRendererSource(5100)),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);
    }

    private static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuPageDecode", fileName));
}
