using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Content /3's token kinds through the build (slice S3b): a consolidation acquired, re-derived under Release 1's rule set
/// (which runs content /3), built into the first mount and written, then the index's own rows read the way the evidence
/// bundle composes a quote. Quoted wording is in the searchable text, between quotation segments, and cited; the
/// publisher's deleted wording is a deleted segment, never in the searchable text, the text a quote carries, its digest or
/// <c>cites</c>; an unmarked note says so.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE INDEX'S ROWS AND NOT THE HANDLER. On this base a re-derived population's index holds its articles and no state:
/// inventory /4 never mints a state coordinate from the file (S2), and the states of a re-derived population are index /9's
/// (S6, in flight as PR #980). The evidence bundle serves a state's articles, so the handler cannot serve these yet; the
/// composition it runs (<see cref="V3CorpusMount.ArticleSegments"/>, <see cref="V3CorpusMount.WithSegments"/>,
/// <see cref="V3CorpusMount.ArticleNotes"/>) is run here over the rows the build wrote.
/// </para>
/// <para>
/// LEGAL CARE. The Luxembourg body is synthetic: every byte of <see cref="Content3Consolidation"/> is written here, in the
/// publisher's shape (the identification block of <see cref="LuxembourgRederivationTests.SyntheticConsolidation"/>), with
/// invented wording. No publisher file is read beyond the retained PDF the shared acquisition fixture serves.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgContent3MountTests
{
    private const string DeletedReference = "/eli/etat/leg/loi/1999/09/09/n9/jo";
    private const string QuotedReference = "/eli/etat/leg/loi/2002/02/02/n2/jo";

    /// <summary>A synthetic consolidation in the publisher's shape whose articles carry content /3's shapes.</summary>
    internal static byte[] Content3Consolidation()
    {
        var synthetic = Encoding.UTF8.GetString(LuxembourgRederivationTests.SyntheticConsolidation());
        var body = synthetic.IndexOf("<body>", StringComparison.Ordinal);
        var end = synthetic.IndexOf("</body>", StringComparison.Ordinal);
        Assert.IsTrue(body > 0 && end > body);
        const string articles =
            "<body>\n"
            + "<article id=\"art_1\"><num>Art. 1er.</num><content><p>À l’article 2, le terme <embeddedText>ancien synthétique</embeddedText> "
            + "est remplacé par <embeddedText>nouveau synthétique</embeddedText>. L’article 3 est remplacé comme suit :"
            + "<embeddedStructure><article><num>Art. 3.</num><content><p>Texte cité de la <ref href=\"" + QuotedReference + "\">loi citée</ref>.</p>"
            + "</content></article></embeddedStructure></p>"
            + "<p>Le point 4 est remplacé :<embeddedStructure scl:hide-quote=\"true\"><p>« 4° texte remplacé ».</p></embeddedStructure></p>"
            + "</content></article>\n"
            + "<article id=\"art_2\"><num>Art. 2.</num><content><p>Le ministre <del>des finances, selon la <ref href=\"" + DeletedReference + "\">loi abrogée</ref>,</del>"
            + "compétent<noteRef href=\"#note_1\"/> statue<br/>dans <span class=\"strike\">trente</span>quinze jours.</p></content></article>\n"
            + "<article id=\"art_3\"><num>Art. 3.</num><content><p>Texte synthétique du troisième article.</p></content></article>\n"
            + "<note id=\"note_1\"><p>Modifié par la loi synthétique.</p></note>\n";
        return Encoding.UTF8.GetBytes(synthetic[..body] + articles + synthetic[end..]);
    }

    [TestMethod]
    public async Task QuotedAndDeletedWordingRoundTripThroughTheBuildAndTheQuoteComposition()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Content3Consolidation());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(verified.Delivered, verified.Detail);

        // R0 (content /1) reads only the plain article; R1 under Release 1's rule set (content /3) reads all three.
        CollectionAssert.AreEqual(
            new[] { "unsupported legal-content element: embeddedText", "unsupported legal-content element: del", null },
            verified.AknLegalContent!.Outcomes.Select(static outcome => outcome.Detail).ToArray());
        var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, LuxembourgRederivationTests.Release1(), CancellationToken.None);
        Assert.AreEqual(LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256, rederived.AknLegalContent!.RuleProfileSha256);
        Assert.IsTrue(rederived.AknLegalContent.Outcomes.All(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted),
            string.Join(" | ", rederived.AknLegalContent.Outcomes.Select(static outcome => outcome.Detail)));

        var clock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-content3-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, clock.GetUtcNow());
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);
            using (var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None))
            {
                Assert.IsNotNull(mount, "the API's own mount opens and verifies an index holding content /3 articles");
            }

            var rows = new Dictionary<string, (string Text, string Tokens)>(StringComparer.Ordinal);
            using (var connection = LuxembourgIndexBuilder.Open(
                       Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT publisher_id, searchable_text, tokens_json, rule_profile_sha256 FROM articles";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
                        Assert.AreEqual(LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256, reader.GetString(3));
                    }
                }

                // The search matches searchable_text: deleted wording is never in it, quoted wording is.
                foreach (var (_, (text, _)) in rows)
                {
                    foreach (var deleted in new[] { "des finances", "loi abrogée", "trente" })
                    {
                        Assert.IsFalse(text.Contains(deleted, StringComparison.Ordinal), deleted);
                    }
                }
                StringAssert.Contains(rows["art_1"].Text, "nouveau synthétique");

                command.CommandText = "SELECT count(*) FROM relations WHERE href = $href";
                command.Parameters.AddWithValue("$href", DeletedReference);
                Assert.AreEqual(0L, command.ExecuteScalar(), "a reference inside deleted wording emits no cites row");
                command.Parameters["$href"].Value = QuotedReference;
                Assert.AreEqual(1L, command.ExecuteScalar(), "a reference inside quoted wording is the act's own and cited");
            }
            Assert.HasCount(3, rows);

            // art_2: as the bundle composes its row. The deleted wording is a segment, never the text; the text is the wording
            // in force, and the note whose reference carries no marker is unmarked.
            var (deletedText, deletedTokens) = rows["art_2"];
            Assert.AreEqual("Art. 2.Le ministre compétent statue dans quinze jours.", deletedText);
            var segmented = false;
            var row = Serialize(V3CorpusMount.WithSegments(new
            {
                text = deletedText,
                text_sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(deletedText))),
                notes = V3CorpusMount.ArticleNotes(deletedTokens),
                official_source = "fixture",
            }, deletedTokens, deletedText, ref segmented));
            Assert.IsTrue(segmented);
            CollectionAssert.AreEqual(new[] { "text", "text_sha256", "notes", "segments", "official_source" },
                row.RootElement.EnumerateObject().Select(static member => member.Name).ToArray());
            CollectionAssert.AreEqual(
                new[]
                {
                    "text:Art. 2.Le ministre ", "deleted_text:des finances, selon la loi abrogée,", "text:compétent statue dans ",
                    "deleted_text:trente", "text:quinze jours.",
                },
                Segments(row.RootElement));
            var note = row.RootElement.GetProperty("notes").EnumerateArray().Single();
            Assert.AreEqual(JsonValueKind.Null, note.GetProperty("marker").ValueKind);
            Assert.AreEqual("Modifié par la loi synthétique.", note.GetProperty("text").GetString());
            Assert.IsTrue(note.GetProperty("unmarked").GetBoolean());

            // art_1: the quoted wording is in the text and between quotation segments, with the publisher's mark evidence.
            var (quotedText, quotedTokens) = rows["art_1"];
            StringAssert.Contains(quotedText, "le terme ancien synthétique est remplacé par nouveau synthétique.");
            var quoted = V3CorpusMount.ArticleSegments(quotedTokens, quotedText)!;
            CollectionAssert.AreEqual(
                new[] { "embeddedText:False", "embeddedText:False", "embeddedStructure:False", "embeddedStructure:True" },
                quoted.Where(static segment => (string)segment!["kind"]! == "quotation_start")
                    .Select(static segment => (string)segment!["quotation"]! + ":" + (bool)segment!["marks_in_wording"]!).ToArray());
            Assert.IsFalse(quoted.Any(static segment => (string)segment!["kind"]! == "deleted_text"));

            // art_3: a shape content /2 admits has no segments, so its row keeps its members and bytes.
            var (plainText, plainTokens) = rows["art_3"];
            Assert.IsNull(V3CorpusMount.ArticleSegments(plainTokens, plainText));
            var plain = new { text = plainText, notes = V3CorpusMount.ArticleNotes(plainTokens) };
            var unsegmented = false;
            Assert.AreSame(plain, V3CorpusMount.WithSegments(plain, plainTokens, plainText, ref unsegmented));
            Assert.IsFalse(unsegmented);
            var answer = new { scope = "s", page_rule = "p", states = Array.Empty<object>() };
            Assert.AreSame(answer, V3CorpusMount.WithSegmentsRule(answer, segmented: false));
            CollectionAssert.AreEqual(new[] { "scope", "page_rule", "segments_rule", "states" },
                Serialize(V3CorpusMount.WithSegmentsRule(answer, segmented: true)).RootElement.EnumerateObject().Select(static member => member.Name).ToArray());

            // A stored stream whose wording in force is not its searchable text is refused, never served.
            Assert.ThrowsExactly<InvalidDataException>(() => V3CorpusMount.ArticleSegments(deletedTokens, deletedText + "x"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonDocument Serialize(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value));

    private static string[] Segments(JsonElement row) =>
        row.GetProperty("segments").EnumerateArray()
            .Select(static segment => segment.GetProperty("kind").GetString() + ":" +
                (segment.TryGetProperty("text", out var text) ? text.GetString() : segment.GetProperty("quotation").GetString()))
            .ToArray();
}
