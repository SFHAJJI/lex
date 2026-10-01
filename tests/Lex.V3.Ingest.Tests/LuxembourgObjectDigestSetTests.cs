using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgObjectDigestSetTests
{
    private static readonly SourceArtifactRef RunIdentity = new(
        "urn:uuid:f30f31b9-4d51-4c25-af77-f9f411146c09", new string('a', 64));

    [TestMethod]
    public void CompactIdentitiesMatchOrdinalStringSetAcrossOrdersDuplicatesAndLookups()
    {
        var distinct = Observations(1_024);
        var delivered = distinct.Reverse().Concat(distinct.Take(300)).ToArray();
        var expected = delivered.Select(Digest).ToHashSet(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var actual = LuxembourgObjectDigestSet.FromObservations(delivered);
        Assert.AreEqual(expected.Length, actual.Count);
        CollectionAssert.AreEqual(expected, actual.ToArray());
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], actual[index]);
            Assert.IsTrue(actual.Contains(expected[index]));
        }
        // These span the unsigned digest range and are absent from the observed set.
        Assert.IsFalse(actual.Contains(new string('0', 64)));
        Assert.IsFalse(actual.Contains(new string('f', 64)));
        for (var index = 0; index < 100; index++)
        {
            var foreign = LuxembourgObservedObjectIdentitySetTests.Observation(
                $"https://data.legilux.lu/eli/foreign/{index}");
            Assert.IsFalse(actual.Contains(Digest(foreign)));
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(32)]
    [DataRow(48)]
    [DataRow(63)]
    public void MembershipChecksTheCompleteDigestIncludingSharedPrefixes(int changedPosition)
    {
        var set = LuxembourgObjectDigestSet.FromObservations(Observations(1));
        var changed = set[0].ToCharArray();
        changed[changedPosition] = changed[changedPosition] == '0' ? '1' : '0';
        Assert.IsFalse(set.Contains(new string(changed)));
        Assert.IsTrue(set.Contains(set[0]));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("A")]
    [DataRow("g")]
    [DataRow(" ")]
    public void MembershipRejectsNonCanonicalDigests(string? character)
    {
        var set = LuxembourgObjectDigestSet.FromObservations(Observations(1));
        var candidate = character is { Length: 1 } ? new string(character[0], 64) : character;
        Assert.IsFalse(set.Contains(candidate));
        Assert.IsFalse(set.Contains(set[0].ToUpperInvariant()));
        Assert.IsFalse(set.Contains(set[0][..^1]));
        Assert.IsFalse(set.Contains(set[0] + "0"));
    }

    [TestMethod]
    public void EmptyAndRepeatedIdentitySetsHaveExactSetSemantics()
    {
        var empty = LuxembourgObjectDigestSet.FromObservations([]);
        Assert.AreEqual(0, empty.Count);
        Assert.IsFalse(empty.Contains(new string('0', 64)));
        Assert.HasCount(0, empty.ToArray());
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = empty[0]);

        var one = Observations(1)[0];
        var repeated = LuxembourgObjectDigestSet.FromObservations(Enumerable.Repeat(one, 100).ToArray());
        Assert.AreEqual(1, repeated.Count);
        Assert.AreEqual(Digest(one), repeated[0]);
        Assert.IsTrue(repeated.Contains(Digest(one)));
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = repeated[-1]);
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = repeated[1]);
    }

    [TestMethod]
    public void CompletedSetDoesNotRetainTheMutableObservationList()
    {
        var observations = Observations(20).ToList();
        var expected = observations.Select(Digest).Order(StringComparer.Ordinal).ToArray();
        var set = LuxembourgObjectDigestSet.FromObservations(observations);
        observations.Clear();
        observations.Add(LuxembourgObservedObjectIdentitySetTests.Observation("https://data.legilux.lu/eli/replacement"));
        CollectionAssert.AreEqual(expected, set.ToArray());
        Assert.IsFalse(set.Contains(Digest(observations[0])));
    }

    [TestMethod]
    public void CompactSetKeepsExistingCanonicalBytesDigestAndIndependentReadback()
    {
        var observations = Observations(200).Reverse().ToArray();
        var oldValues = observations.Select(Digest).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var baseline = new LuxembourgObservedObjectIdentitySet(RunIdentity, oldValues);
        var compact = LuxembourgObservedObjectIdentitySet.FromObservations(RunIdentity, observations);
        using var oldBytes = new MemoryStream();
        using var newBytes = new MemoryStream();
        var oldDigest = LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(oldBytes, baseline);
        var newDigest = LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(newBytes, compact);
        Assert.AreEqual(oldDigest, newDigest);
        CollectionAssert.AreEqual(oldBytes.ToArray(), newBytes.ToArray());
        var reopened = VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(
            new SourceArtifactRef("urn:uuid:462b51af-d12d-4504-b6d2-6adba1bdbd37", newDigest), newBytes.ToArray());
        CollectionAssert.AreEqual(oldValues, reopened.Set.ObjectRefSha256Values.ToArray());
    }

    [TestMethod]
    public void ConstructionRejectsNullObservationsAndCancellation()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgObjectDigestSet.FromObservations(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => LuxembourgObjectDigestSet.FromObservations([null!]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            LuxembourgObjectDigestSet.FromObservations([], cancelled.Token));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void DeclaredCountCannotOmitOrInventAnEnumeratedIdentity(int declaredCount)
    {
        var source = new WrongCountList(Observations(1), declaredCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => LuxembourgObjectDigestSet.FromObservations(source));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(32)]
    [DataRow(48)]
    public void CanonicalReaderChecksFullDigestOrderAndDuplicates(int differingPosition)
    {
        var low = new string('0', 64);
        var highCharacters = low.ToCharArray();
        highCharacters[differingPosition] = '1';
        var high = new string(highCharacters);
        var valid = ReadRawValues(JsonSerializer.Serialize(new[] { low, high }));
        CollectionAssert.AreEqual(new[] { low, high }, valid.Set.ObjectRefSha256Values.ToArray());
        foreach (var invalid in new[] { new[] { high, low }, new[] { low, low } })
        {
            var failure = Assert.ThrowsExactly<ArgumentException>(() => ReadRawValues(JsonSerializer.Serialize(invalid)));
            Assert.AreEqual("canonicalBytes", failure.ParamName);
            StringAssert.Contains(failure.Message, "ordinal-ascending and distinct");
        }
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("number")]
    [DataRow("uppercase")]
    [DataRow("short")]
    [DataRow("nonhex")]
    public void CanonicalReaderRejectsMalformedIdentityValues(string kind)
    {
        var values = kind switch
        {
            "null" => "[null]",
            "number" => "[1]",
            "uppercase" => JsonSerializer.Serialize(new[] { new string('A', 64) }),
            "short" => JsonSerializer.Serialize(new[] { new string('a', 63) }),
            "nonhex" => JsonSerializer.Serialize(new[] { new string('g', 64) }),
            _ => throw new InvalidOperationException()
        };
        var failure = Assert.ThrowsExactly<ArgumentException>(() => ReadRawValues(values));
        Assert.AreEqual("canonicalBytes", failure.ParamName);
        StringAssert.Contains(failure.Message, kind is "null" or "number" ? "non-string" : "lowercase hex SHA-256");
    }

    [TestMethod]
    public void CanonicalReaderValidatesAllValuesBeforeCheckingOrder()
    {
        var values = JsonSerializer.Serialize(new[] { new string('f', 64), new string('0', 64), "bad" });
        var failure = Assert.ThrowsExactly<ArgumentException>(() => ReadRawValues(values));
        StringAssert.Contains(failure.Message, "lowercase hex SHA-256");
    }

    private static VerifiedLuxembourgObservedObjectIdentitySet ReadRawValues(string arrayJson)
    {
        using var values = JsonDocument.Parse(arrayJson);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema = LuxembourgObservedObjectIdentitySet.SchemaId,
            run_identity = new { resource_id = RunIdentity.ResourceId, sha256 = RunIdentity.Sha256 },
            object_ref_sha256 = values.RootElement
        }) + "\n");
        var reference = new SourceArtifactRef("urn:uuid:040f0784-1360-4818-bc50-04d2ab7351f0",
            LuxembourgObservedObjectIdentitySetCanonicalWriter.ComputeSetSha256(bytes));
        return VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify(reference, bytes);
    }

    private sealed class WrongCountList(LuxembourgResourceObservation[] values, int count)
        : IReadOnlyList<LuxembourgResourceObservation>
    {
        public int Count => count;
        public LuxembourgResourceObservation this[int index] => values[index];
        public IEnumerator<LuxembourgResourceObservation> GetEnumerator() =>
            ((IEnumerable<LuxembourgResourceObservation>)values).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static LuxembourgResourceObservation[] Observations(int count) => Enumerable.Range(0, count)
        .Select(index => LuxembourgObservedObjectIdentitySetTests.Observation(
            $"https://data.legilux.lu/eli/compact/{index:D5}")).ToArray();

    private static string Digest(LuxembourgResourceObservation observation) =>
        ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef);
}
