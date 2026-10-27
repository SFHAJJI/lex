using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The names record (<see cref="NamesRecord"/>) and the indexing record a measurement writes (<see cref="IndexingRecord"/>):
/// one form for one measurement, read whole or refused whole, bound to the mount's corpus and index digests. The indexing
/// record is held to the web root's own reader (<c>V3WebRoot.ReadIndexingRecord</c>), which refuses a whole record for one
/// broken rule, so a measurement can never write a record that opens nothing.
/// </summary>
[TestClass]
public sealed class V3NamesRecordTests
{
    private static readonly string Corpus = new('a', 64);
    private static readonly string Luxembourg = new('b', 64);
    private static readonly string Europe = new('c', 64);

    private static NamesMeasuredMount Mount => new(Corpus, Luxembourg, Europe, "2026-10-12T18:17:05Z");

    private static NamesWithheldWork[] Works =>
    [
        new(NamesRecord.LuxembourgPublisher, "http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1", null, null,
            PrivateIndividuals.LuxembourgNaturalisation, [PrivateIndividuals.ClassCode], []),
        new(NamesRecord.EuropePublisher, "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000092", null, "32099R0092",
            PrivateIndividuals.EuropeRestrictiveMeasures, [PrivateIndividuals.BirthDateCode, PrivateIndividuals.ClassCode], [new string('e', 64)]),
        new(NamesRecord.LuxembourgPublisher, "http://data.legilux.public.lu/eli/etat/leg/loi/2020/03/03/a1", "loi-2020-03-03-a1", null,
            PrivateIndividuals.LuxembourgTextNamesPerson, [PrivateIndividuals.BirthDateCode, PrivateIndividuals.PersonNamedCode],
            [new string('d', 64), new string('1', 64)]),
    ];

    [TestMethod]
    public void ARecordIsWrittenInOneFormAndReadBackWhole()
    {
        var first = NamesRecord.Write(Mount, Works);
        var second = NamesRecord.Write(Mount, Works.Reverse().ToArray());
        CollectionAssert.AreEqual(first, second, "the works' order given changes nothing: one form for one measurement");
        var text = Encoding.UTF8.GetString(first);
        Assert.IsFalse(text.Contains('\r', StringComparison.Ordinal), "LF line ends whatever the platform");
        Assert.IsTrue(text.EndsWith("}\n", StringComparison.Ordinal));

        var read = NamesRecord.Read(first, Mount);
        Assert.HasCount(3, read);
        CollectionAssert.AreEqual(
            new[] { "eu-eurlex", "lu-legilux", "lu-legilux" },
            read.Select(static work => work.Publisher).ToArray(),
            "in (publisher, work IRI) order");
        var held = read.Single(static work => work.WorkKey is not null);
        CollectionAssert.AreEqual(new[] { new string('1', 64), new string('d', 64) }, held.StateSha256s.ToArray(), "hashes in order");
        Assert.AreEqual("32099R0092", read[0].Celex);
        Assert.IsEmpty(NamesRecord.Read(NamesRecord.Write(Mount, []), Mount), "a record of no work withholds none");
    }

    [TestMethod]
    public void ARecordOfAnotherBuildIsRefused()
    {
        var bytes = NamesRecord.Write(Mount, Works);
        foreach (var other in new[]
        {
            Mount with { CorpusSha256 = new string('f', 64) },
            Mount with { LuxembourgIndexSha256 = new string('f', 64) },
            Mount with { EuropeIndexSha256 = null },
            Mount with { LuxembourgIndexSha256 = null },
        })
        {
            Assert.ThrowsExactly<InvalidDataException>(() => NamesRecord.Read(bytes, other), other.ToString());
        }

        // The build time is the index's own: the digests bind the record, and another build time is read as written.
        Assert.HasCount(3, NamesRecord.Read(bytes, Mount with { BuiltAt = null }));
    }

    [TestMethod]
    public void ARecordThatBreaksAnyRuleIsRefusedWhole()
    {
        var good = JsonNode.Parse(NamesRecord.Write(Mount, Works))!;
        var broken = new (string What, Action<JsonNode> Break)[]
        {
            ("another schema", static node => node["schema"] = "lex-v3-names-record/2"),
            ("another measure", static node => node["measure"] = "lex-v3-names-measure/0"),
            ("another rule", static node => node["rule"] = "anything"),
            ("an unknown member", static node => node["extra"] = 1),
            ("a missing member", static node => node.AsObject().Remove("works")),
            ("a corpus digest in capitals", static node => node["measured"]!["corpus_sha256"] = Corpus.ToUpperInvariant()),
            ("a build time not in UTC", static node => node["measured"]!["built_at"] = "2026-10-12T18:17:05+02:00"),
            ("works not an array", static node => node["works"] = new JsonObject()),
            ("works out of order", static node => Swap(node["works"]!.AsArray(), 1, 2)),
            ("one work twice", static node => node["works"]!.AsArray()[2] = node["works"]![1]!.DeepClone()),
            ("an unknown publisher", static node => node["works"]![0]!["publisher"] = "de-bgbl"),
            ("a Luxembourg IRI for an EU act", static node => node["works"]![0]!["work_iri"] = "http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n2"),
            ("an EU act with no CELEX", static node => node["works"]![0]!["celex"] = null),
            ("a CELEX on a Luxembourg act", static node => node["works"]![1]!["celex"] = "32099R0092"),
            ("a work key on an EU act", static node => node["works"]![0]!["work_key"] = "loi-2020-03-03-a1"),
            ("a work key of another shape", static node => node["works"]![2]!["work_key"] = "Loi 2020"),
            ("a general class", static node => node["works"]![1]!["class"] = PrivateIndividuals.LuxembourgGeneral),
            ("an EU class on a Luxembourg act", static node => node["works"]![1]!["class"] = PrivateIndividuals.EuropeRestrictiveMeasures),
            ("an unknown class", static node => node["works"]![1]!["class"] = "lu_unknown"),
            ("another reason", static node => node["works"]![1]!["reason"] = "licence"),
            ("no reason code", static node => node["works"]![1]!["evidence"] = new JsonArray()),
            ("an unknown reason code", static node => node["works"]![1]!["evidence"] = new JsonArray("a_name")),
            ("reason codes out of order", static node => node["works"]![2]!["evidence"] = new JsonArray("person_named", "birth_date")),
            ("a state digest that is no digest", static node => node["works"]![2]!["state_sha256s"] = new JsonArray("abc")),
            ("state digests out of order", static node => node["works"]![2]!["state_sha256s"] = new JsonArray(new string('d', 64), new string('1', 64))),
            ("a work as a string", static node => node["works"]![0] = "work"),
        };
        foreach (var (what, breakIt) in broken)
        {
            var node = good.DeepClone();
            breakIt(node);
            var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
            Assert.ThrowsExactly<InvalidDataException>(() => NamesRecord.Read(bytes, Mount), what);
        }

        foreach (var text in new[] { "", "not json", "null", "[]", "{}" })
        {
            Assert.ThrowsExactly<InvalidDataException>(() => NamesRecord.Read(Encoding.UTF8.GetBytes(text), Mount), text);
        }

        Assert.HasCount(3, NamesRecord.Read([.. "﻿"u8.ToArray(), .. Encoding.UTF8.GetBytes(good.ToJsonString())], Mount),
            "a byte-order mark is no part of it, and the record is read whatever its spacing");
    }

    [TestMethod]
    public void AnIndexingRecordAMeasurementWritesIsOneTheWebRootAdmits()
    {
        var bytes = IndexingRecord.Write("2026-10-12T18:17:05Z",
        [
            ("/lu-legilux/loi-2020-03-03-a1", ["fra"]),
            ("/eu-eurlex/32016R0679", ["fra", "eng"]),
            ("/lu-legilux/code-civil", ["fra"]),
        ]);
        CollectionAssert.AreEqual(bytes, IndexingRecord.Write("2026-10-12T18:17:05Z",
        [
            ("/lu-legilux/code-civil", ["fra"]),
            ("/eu-eurlex/32016R0679", ["eng", "fra"]),
            ("/lu-legilux/loi-2020-03-03-a1", ["fra"]),
        ]), "one form whatever the order given");
        var record = V3WebRoot.ReadIndexingRecord(IndexingRecord.Text(bytes));
        Assert.IsNotNull(record, "the web root reads what the measurement writes");
        Assert.AreEqual("2026-10-12T18:17:05Z", record.BuiltAt);
        CollectionAssert.AreEqual(
            new[] { "/eu-eurlex/32016R0679", "/lu-legilux/code-civil", "/lu-legilux/loi-2020-03-03-a1" },
            record.Works.Select(static work => work.Address).ToArray());
        CollectionAssert.AreEqual(new[] { "eng", "fra" }, record.Works[0].Languages.ToArray());
        Assert.IsNotNull(V3WebRoot.ReadIndexingRecord(IndexingRecord.Text(IndexingRecord.Write(null, []))), "a record of no work is a record");

        // What the web root would refuse whole is never written: a treaty's CELEX with "/", a consolidated CELEX, an interface
        // language, a work with no language, a work twice.
        Assert.IsFalse(IndexingRecord.Listable("/eu-eurlex/12016E/TXT"));
        Assert.IsFalse(IndexingRecord.Listable("/eu-eurlex/02016R0679-20160504"));
        Assert.IsFalse(IndexingRecord.Listable("/fr/lu-legilux/code-civil"));
        Assert.IsFalse(IndexingRecord.Listable("/lu-legilux/Code civil"));
        Assert.ThrowsExactly<ArgumentException>(() => IndexingRecord.Write(null, [("/eu-eurlex/12016E/TXT", ["fra"])]));
        Assert.ThrowsExactly<ArgumentException>(() => IndexingRecord.Write(null, [("/lu-legilux/code-civil", [])]));
        Assert.ThrowsExactly<ArgumentException>(() => IndexingRecord.Write(null, [("/lu-legilux/code-civil", ["fr"])]));
        Assert.ThrowsExactly<ArgumentException>(() => IndexingRecord.Write(null, [("/lu-legilux/code-civil", ["fra"]), ("/lu-legilux/code-civil", ["fra"])]));
        Assert.ThrowsExactly<ArgumentException>(() => IndexingRecord.Write("2026-10-12", []));
    }

    private static void Swap(JsonArray array, int first, int second)
    {
        var a = array[first]!.DeepClone();
        var b = array[second]!.DeepClone();
        array[first] = b;
        array[second] = a;
    }
}
