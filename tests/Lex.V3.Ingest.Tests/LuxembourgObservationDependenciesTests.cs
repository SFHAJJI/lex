using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgObservationDependenciesTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private static readonly SourceArtifactRef Observation = new(
        "urn:uuid:00000000-0000-4000-8000-000000000001", new string('1', 64));

    [TestMethod]
    public void ReadsForwardWemiAcrossPartitionsInOriginalBreadthFirstOrder()
    {
        // A partition-local lookup would miss expression, manifestation and file.
        var partitions = new[]
        {
            new[] { Row("act", "isRealizedBy", "expression"), Row("act", "cites", "unrelated") },
            new[] { Row("expression", "isEmbodiedBy", "manifestation") },
            new[] { Row("manifestation", "isExemplifiedBy", "file") },
            new[] { Row("file", "userFormat", "xml"), Row("unrelated", "userFormat", "pdf") },
        };
        var lookups = new List<string>();
        var result = LuxembourgObservationDependencies.Collect("act", Observation, subject =>
        {
            lookups.Add(subject);
            return partitions.SelectMany(p => p).Where(a => a.SubjectIri == subject).ToArray();
        });
        CollectionAssert.AreEqual(new[] { "act", "expression", "manifestation", "file" }, lookups);
        CollectionAssert.AreEqual(partitions.SelectMany(p => p).Where(a => a.SubjectIri != "unrelated").ToArray(),
            result.ToArray());
        Assert.AreSame(partitions[0][0], result[0], "The memory lookup keeps the original assertion instance.");
    }

    [TestMethod]
    public void CyclesAndSharedDescendantsAreVisitedOnce()
    {
        var rows = new[] { Row("act", "isRealizedBy", "a"), Row("act", "isRealizedBy", "b"),
            Row("a", "isEmbodiedBy", "shared"), Row("b", "isEmbodiedBy", "shared"),
            Row("shared", "isExemplifiedBy", "act") };
        var lookups = new List<string>();
        var result = LuxembourgObservationDependencies.Collect("act", Observation, key =>
        {
            lookups.Add(key);
            return rows.Where(row => row.SubjectIri == key).ToArray();
        });
        CollectionAssert.AreEqual(new[] { "act", "a", "b", "shared" }, lookups);
        CollectionAssert.AreEqual(rows, result.ToArray());
    }

    [TestMethod]
    public void LiteralEdgesAndNonWemiRelationsDoNotTriggerLookups()
    {
        var rows = new[] { Row("act", "isRealizedBy", "literal", LuxembourgAssertionObjectKind.Literal),
            Row("act", "cites", "target"), Row("act", "previousIsExemplifiedBy", "previous") };
        var result = LuxembourgObservationDependencies.Collect("act", Observation, key =>
        {
            Assert.AreEqual("act", key);
            return rows;
        });
        CollectionAssert.AreEqual(rows, result.ToArray());
    }

    [TestMethod]
    public void MissingDescendantKeepsItsLinkWithoutInventingAssertions()
    {
        var row = Row("act", "isRealizedBy", "missing");
        var result = LuxembourgObservationDependencies.Collect("act", Observation,
            key => key == "act" ? [row] : []);
        CollectionAssert.AreEqual(new[] { row }, result.ToArray());
        Assert.AreEqual(0, LuxembourgObservationDependencies.Collect("absent", Observation, _ => []).Count);
    }

    [TestMethod]
    public void ConsolidationReadsOnlyTheUniqueOriginalActsOwnAssertions()
    {
        var root = new[] { Type("consolidation", "Consolidation"), Row("consolidation", "isMemberOf", "work") };
        var original = new[] { Type("work/jo", "Act"), Row("work/jo", "isRealizedBy", "original-expression") };
        var lookups = new List<string>();
        var result = LuxembourgObservationDependencies.Collect("consolidation", Observation, key =>
        {
            lookups.Add(key);
            return key switch { "consolidation" => root, "work/jo" => original, _ => throw new InvalidOperationException(key) };
        });
        CollectionAssert.AreEqual(new[] { "consolidation", "work/jo" }, lookups);
        CollectionAssert.AreEqual(root.Concat(original).ToArray(), result.ToArray());
    }

    [TestMethod]
    public void AmbiguousParentsAndNonConsolidationsDoNotInventAnOriginalAct()
    {
        foreach (var root in new[]
        {
            new[] { Type("root", "Consolidation"), Row("root", "isMemberOf", "one"), Row("root", "isMemberOf", "two") },
            new[] { Type("root", "Act"), Row("root", "isMemberOf", "one") },
            new[] { Type("root", "Consolidation") },
        })
        {
            var result = LuxembourgObservationDependencies.Collect("root", Observation, key =>
            {
                Assert.AreEqual("root", key);
                return root;
            });
            CollectionAssert.AreEqual(root, result.ToArray());
        }
    }

    [TestMethod]
    public void OriginalActAlreadyVisitedThroughWemiIsNotDuplicated()
    {
        var root = new[] { Type("root", "Consolidation"), Row("root", "isMemberOf", "work"),
            Row("root", "isRealizedBy", "work/jo") };
        var original = Type("work/jo", "Act");
        var lookups = new List<string>();
        var result = LuxembourgObservationDependencies.Collect("root", Observation, key =>
        {
            lookups.Add(key);
            return key == "root" ? root : [original];
        });
        CollectionAssert.AreEqual(new[] { "root", "work/jo" }, lookups);
        CollectionAssert.AreEqual(root.Append(original).ToArray(), result.ToArray());
    }

    [TestMethod]
    public void LookupCannotSubstituteAnotherSubjectOrRun()
    {
        var wrongSubject = Row("other", "isRealizedBy", "expression");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LuxembourgObservationDependencies.Collect("act", Observation, _ => [wrongSubject]));
        var wrongRun = new LuxembourgObservedAssertion("act", Jolux + "isRealizedBy",
            LuxembourgAssertionObjectKind.Iri, "expression", "", "",
            new SourceArtifactRef(Observation.ResourceId, new string('2', 64)));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LuxembourgObservationDependencies.Collect("act", Observation, _ => [wrongRun]));
    }

    private static LuxembourgObservedAssertion Row(string subject, string predicate, string value,
        LuxembourgAssertionObjectKind kind = LuxembourgAssertionObjectKind.Iri) =>
        new(subject, Jolux + predicate, kind, value, "", "", Observation);

    private static LuxembourgObservedAssertion Type(string subject, string type) =>
        new(subject, RdfType, LuxembourgAssertionObjectKind.Iri, Jolux + type, "", "", Observation);
}
