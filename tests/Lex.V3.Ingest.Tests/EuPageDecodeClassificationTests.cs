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
/// Every body here is a REAL RETAINED PUBLISHER RESPONSE, carried in as a fixture with its digest
/// pinned, so an edited fixture fails on the digest before it is ever decoded. The four page bodies
/// were identical across two canary runs forty minutes apart and a third probe the next day, which
/// is what made the failure debuggable without touching the publisher again.
/// </para>
/// <para>
/// WHICH QUERY EACH IS EVIDENCE OF, because a fixture that silently describes a query nobody sends
/// any more is the next reader's wrong assumption. FIVE OF THE SIX ARE PRE-FIX. They are the
/// publisher's answers to the ObjectFacts page template as it stood at 25f4990d, whose value-derived
/// cursor key read <c>BIND(IF(BOUND(?value), STR(?value), "") AS ?key_4)</c>. That form is GONE:
/// D1-05f part two replaced it with <c>COALESCE(STR(?value), "")</c>, so those five are NOT BYTE
/// CURRENT for the query this route now sends. They are kept exactly as retained because they are
/// the evidence of the fault and of part one's classification, and part two's reader tests run
/// against them for that reason.
/// </para>
/// <para>
/// THE ONE POST-FIX BODY is <c>objectfacts-page-coalesce-total.bin</c>, the publisher's answer to
/// the COALESCE form over the same batch, retained by the bounded probe that settled part two's
/// shape. Any page shape these cannot represent becomes a new fixture from the final canary run,
/// with its digest pinned there.
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
    [DataRow("objectfacts-page-coalesce-total.bin", "3ee1711425945b2ec789fdffe6d66c3a12ea6527c91a3ac58a531e1eb65afa80", 39866)]
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
    /// D1-05f part two's closure at the reader: EVERY PROJECTED CURSOR VARIABLE IS BOUND IN EVERY
    /// BINDING of the post-fix page, and the page still carries all 41 rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture is the publisher's own answer to the COALESCE form of the query, retained by the
    /// bounded probe that settled part two's shape (PROBE
    /// lex-event-20260905T015937388Z-8bc0d2893047464c91a6a1c54982b5e1). It is the SAME BATCH and the
    /// same 41 rows as the pre-fix page beside it, differing only in that one BIND.
    /// </para>
    /// <para>
    /// THE OTHER HALF OF THIS ASSERTION MATTERS AS MUCH AS THE FIRST. <c>?value</c> IS STILL ABSENT
    /// on exactly the eight unbound rows, and must be: that absence IS the unbound fact, carried
    /// with <c>value_kind</c> of <c>"unbound"</c>. Only the CURSOR is totalised. A change that had
    /// totalised both would have destroyed the fact while appearing to succeed, and would have
    /// passed a test that merely counted absences, which is why this one counts them BY VARIABLE.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryCursorVariableIsBoundInEveryBindingOfThePostFixPage()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            ReadFixture("objectfacts-page-coalesce-total.bin"));
        var root = document.RootElement;
        var variables = root.GetProperty("head").GetProperty("vars")
            .EnumerateArray().Select(static value => value.GetString()!).ToArray();
        var bindings = root.GetProperty("results").GetProperty("bindings").EnumerateArray().ToArray();

        Assert.HasCount(41, bindings, "the COALESCE form must deliver the same rows, not fewer.");

        var cursorVariables = variables.Where(static name => name.StartsWith("key_", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(6, cursorVariables);

        foreach (var name in cursorVariables)
        {
            var absent = bindings.Count(binding => !binding.TryGetProperty(name, out _));
            Assert.AreEqual(
                0,
                absent,
                $"{name} is absent from {absent} bindings; a cursor variable must be total, and a "
                + "page where one is not must refuse as PageDecodeFailed naming us.");
        }

        // And the unbound fact survives, in the two places that carry it.
        Assert.AreEqual(
            8,
            bindings.Count(static binding => !binding.TryGetProperty("value", out _)),
            "value must STILL be absent on the unbound rows: that absence is the fact itself.");
        Assert.AreEqual(
            8,
            bindings.Count(static binding =>
                binding.GetProperty("value_kind").GetProperty("value").GetString() == "unbound"),
            "and value_kind must still say unbound on exactly those rows.");
    }

    /// <summary>
    /// The pre-fix and post-fix pages are the same 41 rows and differ in exactly the one variable
    /// the template change targeted, so the fix is not quietly changing what the query selects.
    /// </summary>
    [TestMethod]
    public void TheFixChangedTheCursorKeyAndNothingElseAboutTheSelection()
    {
        var before = AbsenceCountsByVariable("objectfacts-page-unbound-key.bin");
        var after = AbsenceCountsByVariable("objectfacts-page-coalesce-total.bin");

        CollectionAssert.AreEquivalent(
            before.Keys.ToArray(), after.Keys.ToArray(), "the projection must be unchanged.");
        Assert.AreEqual(8, before["key_4"]);
        Assert.AreEqual(0, after["key_4"], "the one variable the change targeted.");

        foreach (var name in before.Keys.Where(static name => name != "key_4"))
        {
            Assert.AreEqual(
                before[name],
                after[name],
                $"{name} must be untouched by a change aimed only at key_4.");
        }
    }

    private static Dictionary<string, int> AbsenceCountsByVariable(string fileName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadFixture(fileName));
        var root = document.RootElement;
        var variables = root.GetProperty("head").GetProperty("vars")
            .EnumerateArray().Select(static value => value.GetString()!).ToArray();
        var bindings = root.GetProperty("results").GetProperty("bindings").EnumerateArray().ToArray();
        return variables.ToDictionary(
            static name => name,
            name => bindings.Count(binding => !binding.TryGetProperty(name, out _)),
            StringComparer.Ordinal);
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
