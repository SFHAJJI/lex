using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuLocatedAmendmentProducerTests
{
    [TestMethod]
    public void CanonicalLocatedAmendmentBytesIgnoreEquivalentDeliveryOrder()
    {
        var admitted = Observation(Held, 21);
        var ambiguous = Observation(Held, 22, sources: [Source, OtherSource]);
        var outside = Observation(Held, 23, Outside);
        var malformed = Observation(Held, 24, properties: Properties(includeRole: false));
        var forward = EuLocatedAmendmentProducer.Produce(
            [admitted, ambiguous, outside, malformed], CompleteCorpus(Source, Held));

        var reorderedAmbiguous = Observation(
            Held,
            22,
            sources: [OtherSource, Source]);
        var reverse = EuLocatedAmendmentProducer.Produce(
            [malformed, outside, reorderedAmbiguous, admitted], CompleteCorpus(Source, Held));

        var first = Write(forward);
        var second = Write(reverse);

        CollectionAssert.AreEqual(first.Bytes, second.Bytes);
        Assert.AreEqual(first.Sha256, second.Sha256);
    }

    [TestMethod]
    public void CanonicalLocatedAmendmentBytesCarryEveryPartitionAndProofCoordinate()
    {
        var production = EuLocatedAmendmentProducer.Produce(
            [
                Observation(Held, 31),
                Observation(Held, 32, sources: [Source, OtherSource]),
                Observation(Held, 33, Outside),
                Observation(Held, 34, properties: Properties(includeRole: false)),
            ],
            CompleteCorpus(Source, Held));

        var canonical = Write(production);
        using var document = JsonDocument.Parse(canonical.Bytes);
        var root = document.RootElement;

        Assert.AreEqual(
            EuLocatedAmendmentProductionCanonicalWriter.Schema,
            root.GetProperty("schema").GetString());
        Assert.AreEqual(1, root.GetProperty("admitted").GetArrayLength());
        Assert.AreEqual(1, root.GetProperty("ambiguous").GetArrayLength());
        Assert.AreEqual(2, root.GetProperty("excluded").GetArrayLength());
        Assert.AreEqual(
            "unmeasured",
            root.GetProperty("coverage").GetProperty("state").GetString());

        var admitted = root.GetProperty("admitted")[0];
        Assert.AreEqual("eu-eurlex", admitted.GetProperty("source_identity").GetProperty("publisher").GetString());
        Assert.IsGreaterThan(0, admitted.GetProperty("source_identity").GetProperty("identifiers").GetArrayLength());
        Assert.AreEqual("eu-eurlex", admitted.GetProperty("target_identity").GetProperty("publisher").GetString());
        Assert.IsGreaterThan(0, admitted.GetProperty("target_identity").GetProperty("identifiers").GetArrayLength());
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("predicate_iri").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("source_observation_id").GetString()));
        Assert.AreEqual(
            "body_in_scope_held",
            admitted.GetProperty("target_body_scope").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("remote_axiom_id").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("location").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("role").GetProperty("code").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("role").GetProperty("authority_uri").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(admitted.GetProperty("type_of_link_target").GetString()));
        Assert.AreEqual(
            64,
            admitted.GetProperty("interpretation_profile_ref").GetProperty("sha256").GetString()!.Length);

        var observation = admitted.GetProperty("observation");
        Assert.IsFalse(string.IsNullOrWhiteSpace(observation.GetProperty("axiom_iri").GetString()));
        Assert.AreEqual(1, observation.GetProperty("annotated_source_iris").GetArrayLength());
        Assert.IsFalse(string.IsNullOrWhiteSpace(observation.GetProperty("annotated_property_iri").GetString()));
        Assert.AreEqual(1, observation.GetProperty("annotated_target_iris").GetArrayLength());
        Assert.AreEqual(3, observation.GetProperty("raw_properties").GetArrayLength());
        foreach (var property in observation.GetProperty("raw_properties").EnumerateArray())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(property.GetProperty("predicate_iri").GetString()));
            var value = property.GetProperty("value");
            Assert.AreEqual("literal", value.GetProperty("kind").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(value.GetProperty("value").GetString()));
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("datatype").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("language").ValueKind);
        }
        Assert.AreEqual(
            1,
            observation.GetProperty("interpretation_profile_refs").GetArrayLength());

        var ambiguity = root.GetProperty("ambiguous")[0];
        Assert.AreEqual(2, ambiguity.GetProperty("candidate_instrument_iris").GetArrayLength());

        var exclusions = root.GetProperty("excluded").EnumerateArray().ToArray();
        var excludedKinds = exclusions.Select(item => item.GetProperty("kind").GetString()).ToArray();
        CollectionAssert.Contains(excludedKinds, "source_outside_verified_corpus");
        CollectionAssert.Contains(excludedKinds, "projection_refused");
        var projectionRefused = exclusions.Single(
            item => item.GetProperty("kind").GetString() == "projection_refused");
        Assert.AreEqual(
            "required_qualifier_missing",
            projectionRefused.GetProperty("projection_refusal").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(projectionRefused.GetProperty("detail").GetString()));
    }

    [TestMethod]
    public void CanonicalLocatedAmendmentDigestChangesWithLineageOrDisposition()
    {
        var held = EuLocatedAmendmentProducer.Produce(
            [Observation(Held, 41)], CompleteCorpus(Source, Held));
        var pending = EuLocatedAmendmentProducer.Produce(
            [Observation(Pending, 41)], CompleteCorpus(Source, Held, Pending));
        var differentProof = EuLocatedAmendmentProducer.Produce(
            [Observation(Held, 42)], CompleteCorpus(Source, Held));

        Assert.AreNotEqual(Write(held).Sha256, Write(pending).Sha256);
        Assert.AreNotEqual(Write(held).Sha256, Write(differentProof).Sha256);
    }

    [TestMethod]
    public void CanonicalLocatedAmendmentDigestIsDomainSeparatedFromTheCanonicalBytes()
    {
        var canonical = Write(EuLocatedAmendmentProducer.Produce(
            [Observation(Held, 51)], CompleteCorpus(Source, Held)));
        var domain = Encoding.ASCII.GetBytes(
            EuLocatedAmendmentProductionCanonicalWriter.Schema + "\n");
        var material = new byte[domain.Length + canonical.Bytes.Length];
        domain.CopyTo(material, 0);
        canonical.Bytes.CopyTo(material, domain.Length);

        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(material)),
            canonical.Sha256);
    }

    private static (byte[] Bytes, string Sha256) Write(EuLocatedAmendmentProduction production)
    {
        using var stream = new MemoryStream();
        var sha256 = EuLocatedAmendmentProductionCanonicalWriter.Write(stream, production);
        return (stream.ToArray(), sha256);
    }
}
