using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Rebuilds a Luxembourg run's final scope manifest from nothing but what the run retained and cited, and requires it
/// to equal the manifest the adapter wrote, byte for byte. The live canary and the default suite call this one
/// function, so a replay that has never met real data is not the first thing a publisher run finds out.
/// </summary>
/// <remarks>
/// Every input here is read from the retained rights index and the retained in-file index that the final manifest's
/// ordered evidence names: the observed objects, their assertions and their relations. The first live run failed
/// because this replay handed the resolver no relations while the adapter's observations carried four, and the retained
/// index had no relations to hand it, so the manifest's relation selector could not be re-derived from its own cited
/// evidence. The index now retains them and names the delivery of every family they came from, and this function asserts
/// both, the second by family name and not by count.
/// </remarks>
internal static class LuxembourgRetainedRunReplay
{
    internal const string RightsIndexSchema = "lex-lu-sparql-rights-evidence/2";
    internal const string InFileIndexSchema = "lex-lu-in-file-rights-evidence/1";

    internal static async Task ReplayAsync(ICustodyStore store,
        VerifiedLuxembourgSourceProfile profile, LuxembourgQueryExecutionResult result,
        IReadOnlyList<string> assertionFamilyKeys, IReadOnlyList<string> relationFamilyKeys, string? expectedManifestation)
    {
        Assert.IsNotNull(result.ScopeManifestReceipt);
        var finalBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            result.ScopeManifestReceipt.Reference.ContentSha256, CancellationToken.None);
        using var manifest = JsonDocument.Parse(finalBytes);
        var indexes = new Dictionary<string, (SourceArtifactRef Ref, JsonElement Json)>(StringComparer.Ordinal);
        foreach (var artifact in manifest.RootElement.GetProperty("ordered_evidence_artifacts").EnumerateArray())
        {
            var reference = new SourceArtifactRef(artifact.GetProperty("resource_id").GetString()!, artifact.GetProperty("sha256").GetString()!);
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, reference.Sha256, CancellationToken.None);
            if (bytes.Length == 0 || bytes.Span[0] != (byte)'{')
                continue;
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.String)
                indexes[schema.GetString()!] = (reference, document.RootElement.Clone());
        }
        Assert.IsTrue(indexes.ContainsKey(RightsIndexSchema),
            $"The final manifest must cite a retained rights index of schema {RightsIndexSchema}; it cites {string.Join(", ", indexes.Keys)}.");
        var sparql = indexes[RightsIndexSchema];
        var inFile = indexes[InFileIndexSchema];

        var citedDeliveries = sparql.Json.GetProperty("deliveries").EnumerateArray()
            .Select(delivery => delivery.GetProperty("PartitionKey").GetString()!).ToArray();
        foreach (var key in assertionFamilyKeys.Concat(relationFamilyKeys))
            CollectionAssert.Contains(citedDeliveries, key,
                $"The retained rights index must name the delivery of family '{key}'; it names {string.Join(", ", citedDeliveries)}.");

        var acquisitionBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            inFile.Json.GetProperty("acquisitionManifestContentSha256").GetString()!, CancellationToken.None);
        var acquisitionRef = JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("acquisitionManifestRef"))!;
        Assert.AreEqual(acquisitionRef.Sha256, ScopeManifestCanonicalWriter.ComputeManifestSha256(acquisitionBytes.Span));
        var run = profile.Snapshot.ObservationRef;
        Assert.AreEqual(run, JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("runIdentity")),
            "The retained in-file index must identify this exact observation run.");
        var readings = inFile.Json.GetProperty("readings").EnumerateArray().ToArray();
        Assert.IsNotEmpty(readings);
        var channelTwo = new List<LuxembourgRightsChannelObservation>();
        foreach (var reading in readings)
        {
            var bodyRef = JsonSerializer.Deserialize<SourceArtifactRef>(reading.GetProperty("BodyRef"))!;
            await CustodyRestore.ReadByDigestCheckedAsync(store, bodyRef.Sha256, CancellationToken.None);
            Assert.AreEqual((int)LuxembourgInFileRightsReadStatus.Observed, reading.GetProperty("Status").GetInt32());
            channelTwo.Add(new(reading.GetProperty("ManifestationIri").GetString()!, run, bodyRef,
                reading.GetProperty("LicenceIris").EnumerateArray().Select(value => value.GetString()!).ToArray()));
        }
        foreach (var acquisition in result.DocumentAcquisitionOutcomesByOrdinal!.Values.Where(value => value.Receipt is not null))
            Assert.IsTrue(channelTwo.Any(channel => channel.EvidenceRef.Sha256 == acquisition.Receipt!.Reference.ContentSha256),
                "Every held body must have this run's retained in-file channel reading.");
        var observations = sparql.Json.GetProperty("observations").EnumerateArray().Select(row =>
        {
            var objectRef = JsonSerializer.Deserialize<SourceObjectRef>(row.GetProperty("ObjectRef"))!;
            var assertions = JsonSerializer.Deserialize<LuxembourgObservedAssertion[]>(row.GetProperty("Assertions"))!;
            var relations = JsonSerializer.Deserialize<LuxembourgObservedRelation[]>(row.GetProperty("Relations"))!;
            var channelOne = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(assertions, run)
                .Select(value => new LuxembourgRightsChannelObservation(value.ManifestationIri, run, sparql.Ref, value.LicenceIris)).ToArray();
            return new LuxembourgResourceObservation(objectRef, run, assertions, relations,
                new LuxembourgSparqlRightsChannelObservations(run, sparql.Ref, channelOne),
                new LuxembourgInFileRightsChannelObservations(run, inFile.Ref,
                    channelTwo.Where(value => assertions.Any(assertion => assertion.SubjectIri == value.ManifestationIri)).ToArray(), true));
        }).ToArray();
        var proofs = assertionFamilyKeys.Select(key => result.FamilyOutcomes.Single(outcome => outcome.FamilyKey == key).Proof).ToArray();
        Assert.IsTrue(proofs.All(proof => proof is not null));
        var replay = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireAllProven(proofs.Select(proof => proof!).ToArray(), observations)));
        if (expectedManifestation is not null)
        {
            Assert.IsTrue(channelTwo.Any(channel => channel.ManifestationIri == expectedManifestation),
                "The selected plain XML manifestation must have an actually held body and same-run in-file reading.");
            Assert.IsTrue(observations.SelectMany(observation => observation.Assertions).Any(assertion =>
                assertion.SubjectIri == expectedManifestation &&
                assertion.PredicateIri == "http://data.legilux.public.lu/resource/ontology/jolux#userFormat" &&
                assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri &&
                assertion.ObjectIriOrLexical == "http://data.legilux.public.lu/resource/authority/user-format/xml"),
                "Plain XML must come from this run's proven publisher assertions, not its URL suffix.");
        }
        foreach (var channel in channelTwo)
        {
            var rights = replay.Resources.SelectMany(resource => resource.BodyJoin.Candidates)
                .Where(candidate => candidate.WemiCandidate.ManifestationIri == channel.ManifestationIri).ToArray();
            Assert.IsNotEmpty(rights, "Retained in-file declaration must resolve onto this run's proven WEMI graph.");
            Assert.IsTrue(rights.All(candidate => candidate.RightsResolution.Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy),
                $"Same-run dual-channel agreement not established for {channel.ManifestationIri}");
        }
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(store,
            profile.Snapshot.CompleteEnumerationRef, observations, replay.OrderedEvidenceArtifacts, CancellationToken.None);
        var rebuilt = profile.ReduceScope(replay, resolver, LuxembourgQueryExecutionAdapter.MintDocumentFetchAddresses(replay)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToScopeManifestFetchAddress()));
        using var stream = new MemoryStream();
        Assert.AreEqual(result.ScopeManifestCanonicalSha256, ScopeManifestCanonicalWriter.Write(stream, rebuilt));
        CollectionAssert.AreEqual(finalBytes.ToArray(), stream.ToArray(), "Final manifest must replay byte-for-byte from retained channels.");
        var finalRef = result.CorpusRecordSet!.Set.ManifestRef;
        VerifiedScopeManifest.ParseAndVerify(finalRef, finalBytes.Span, resolver);
    }
}
