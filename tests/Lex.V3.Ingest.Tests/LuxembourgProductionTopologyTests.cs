using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgProductionTopologyTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Work = "http://data.legilux.public.lu/eli/etat/leg/code/code_civil";
    private const string Expression = Work + "/fr";
    private const string Manifestation = Expression + "/xml";
    private const string Item = "http://data.legilux.public.lu/filestore/eli/etat/leg/code/code_civil/fr/xml/code_civil.xml";

    [TestMethod]
    [DataRow(null, "agreed")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "agreed")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "conflict")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "missing")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "malformed")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "mismatched")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "unicode_item")]
    [DataRow("http://creativecommons.org/licenses/by/4.0/", "escaped_item")]
    public async Task AWorkAcquiresItsBodyFromTheDeliveredMultiSubjectWemiGraph(string? licenceIri, string inFileCase)
    {
        // Only HTTP and the storage backend are test doubles. The public adapter must prove both
        // enumerations, derive observations, resolve WEMI, reduce scope, and acquire the document.
        // Supplying a hand-built work observation would bypass the production grouping defect.
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var profileReceipt = await store.CreateAsync(
            "synthetic vocabulary observation for the topology regression"u8.ToArray(),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var profileEvidence = new SourceArtifactRef(NewUrn(), profileReceipt.Reference.ContentSha256);
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            profileEvidence, profileEvidence, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var item = inFileCase switch
        {
            "unicode_item" => Item.Replace("code_civil.xml", "codé.xml", StringComparison.Ordinal),
            "escaped_item" => Item.Replace("code_civil.xml", "%63ode.xml", StringComparison.Ordinal),
            _ => Item,
        };
        (string Subject, string Predicate, string Value)[] assertions =
        [
            (Work, RdfType, Jolux + "Act"),
            (Work, Jolux + "typeDocument", "http://data.legilux.public.lu/resource/authority/resource-type/TC"),
            (Work, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", Manifestation),
            (Manifestation, RdfType, Jolux + "Manifestation"),
            (Manifestation, Jolux + "userFormat", "http://data.legilux.public.lu/resource/authority/user-format/xml-akomantoso"),
            (Manifestation, Jolux + "isExemplifiedBy", item),
        ];
        if (licenceIri is not null)
        {
            assertions = [.. assertions, (Manifestation, Jolux + "license", licenceIri)];
        }
        var topology = LuxembourgWemiTopology.Resolve(Work, assertions.Select(assertion =>
            new LuxembourgObservedAssertion(assertion.Subject, assertion.Predicate,
                LuxembourgAssertionObjectKind.Iri, assertion.Value, "", "", profileEvidence)).ToArray(), profileEvidence);
        Assert.HasCount(1, topology.Candidates);
        Assert.AreEqual(LuxembourgWemiCandidateDisposition.StructurallyConsistent, topology.Candidates[0].Disposition,
            "The delivered graph itself is a valid body candidate before production splits it by subject.");
        var assertionPage = AssertionRows(assertions);
        var censusPage = LuxembourgAcquisitionTestFixture.RowsJson(Work, Expression, Manifestation);
        var xml = LuxembourgInFileRightsReaderTests.Document().Replace(
            "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226/fr/xml", Manifestation,
            StringComparison.Ordinal);
        xml = inFileCase switch
        {
            "conflict" => xml.Replace("http://creativecommons.org/licenses/by/4.0/", "https://example.invalid/unruled", StringComparison.Ordinal),
            "missing" => xml.Replace("<s:jolux s:name=\"license\">http://creativecommons.org/licenses/by/4.0/</s:jolux>", "", StringComparison.Ordinal),
            "malformed" => xml + "<unclosed>",
            "mismatched" => xml.Replace(Manifestation, Manifestation + "/other", StringComparison.Ordinal),
            _ => xml,
        };
        var documentBytes = Encoding.UTF8.GetBytes(xml);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(3)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(request, censusPage),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 or 14 => Response(request, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(assertions.Length)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(request, assertionPage),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(request, AssertionRows([])),
            15 => DocumentResponse(request),
            _ => throw new AssertFailedException($"Unexpected HTTP request {ordinal}: {request.Method} {request.RequestUri}"),
        });
        var executor = new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new LuxembourgQueryExecutionAdapter(store, executor, profile);
        var (censusRequest, censusWitness) = Partition("S", "census");
        var (assertionRequest, assertionWitness) = Partition("A", "assertions");

        var result = await adapter.RunAsync(
            [(censusRequest, censusWitness, null), (assertionRequest, assertionWitness, null)],
            null, "census", "assertions", LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(420),
            CancellationToken.None);

        Assert.IsNull(result.Refusal, $"{result.Refusal?.Code}: {result.Refusal?.Detail}; families: " +
            JsonSerializer.Serialize(result.FamilyOutcomes.Select(outcome => new
                { outcome.FamilyKey, outcome.Kind, outcome.ExecutorRefusal, outcome.ProofRefusal })));
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        CollectionAssert.AreEqual(new[] { Work, Expression, Manifestation }, result.ResourceObservationSubjects.ToArray());
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.HasCount(1, result.DocumentAcquisitionOutcomesByOrdinal,
            "The work must see its expression and manifestation assertions from this run's proven family.");
        var acquired = result.DocumentAcquisitionOutcomesByOrdinal.Values.Single();
        Assert.IsNotNull(acquired.Receipt, $"Document acquisition refused: {acquired.Refusal}");
        var restored = await store.ReadAsync(acquired.Receipt.Reference, CancellationToken.None);
        CollectionAssert.AreEqual(documentBytes, restored.ToArray());
        Assert.IsNotNull(result.CorpusRecordSet);
        Assert.AreEqual(CorpusBodyRecordKind.Held,
            result.CorpusRecordSet.Set.Records.Single(record => record.ObjectRef.PublisherUri == Work).Body.Kind);
        Assert.IsNotNull(result.ScopeManifestReceipt);
        var finalManifest = Encoding.UTF8.GetString((await store.ReadAsync(
            result.ScopeManifestReceipt.Reference, CancellationToken.None)).Span);
        using var manifestJson = JsonDocument.Parse(finalManifest);
        JsonElement? inFileIndex = null;
        JsonElement? sparqlIndex = null;
        SourceArtifactRef? inFileRef = null;
        SourceArtifactRef? sparqlRef = null;
        foreach (var artifact in manifestJson.RootElement.GetProperty("ordered_evidence_artifacts").EnumerateArray())
        {
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
                artifact.GetProperty("sha256").GetString()!, CancellationToken.None);
            var text = Encoding.UTF8.GetString(bytes.Span);
            if (text.Contains("lex-lu-sparql-rights-evidence/1", StringComparison.Ordinal))
            {
                using var source = JsonDocument.Parse(bytes);
                sparqlIndex = source.RootElement.Clone();
                sparqlRef = new SourceArtifactRef(artifact.GetProperty("resource_id").GetString()!,
                    artifact.GetProperty("sha256").GetString()!);
            }
            if (!text.Contains("lex-lu-in-file-rights-evidence/1", StringComparison.Ordinal))
            {
                continue;
            }
            using var index = JsonDocument.Parse(bytes);
            inFileIndex = index.RootElement.Clone();
            inFileRef = new SourceArtifactRef(artifact.GetProperty("resource_id").GetString()!,
                artifact.GetProperty("sha256").GetString()!);
        }
        Assert.IsNotNull(inFileIndex, "The final manifest must cite this run's retained in-file evidence index.");
        Assert.IsTrue(inFileIndex.Value.TryGetProperty("acquisitionManifestContentSha256", out var acquisitionDigest),
            "The rights index must locate the retained acquisition plan without scanning the custody store.");
        var acquisitionBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            acquisitionDigest.GetString()!, CancellationToken.None);
        var acquisitionRef = JsonSerializer.Deserialize<SourceArtifactRef>(
            inFileIndex.Value.GetProperty("acquisitionManifestRef"))!;
        Assert.AreEqual(acquisitionRef.Sha256, ScopeManifestCanonicalWriter.ComputeManifestSha256(acquisitionBytes.Span));
        var readings = inFileIndex.Value.GetProperty("readings").EnumerateArray().ToArray();
        Assert.HasCount(1, readings, "The held body must retain the selected publisher item's rights reading.");
        var reading = readings.Single();
        Assert.AreEqual(Manifestation, reading.GetProperty("ManifestationIri").GetString());
        Assert.AreEqual(acquired.Receipt.Reference.ContentSha256,
            reading.GetProperty("BodyRef").GetProperty("Sha256").GetString());
        var expectedRead = inFileCase switch
        {
            "malformed" => LuxembourgInFileRightsReadStatus.MalformedXml,
            "mismatched" => LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch,
            _ => LuxembourgInFileRightsReadStatus.Observed,
        };
        Assert.AreEqual((int)expectedRead, reading.GetProperty("Status").GetInt32());

        // Replay the final scope from its retained channels. A retained pre-fetch manifest or a
        // fabricated second reference cannot reproduce this independently reduced final form.
        Assert.IsNotNull(sparqlIndex);
        var bodyRef = JsonSerializer.Deserialize<SourceArtifactRef>(reading.GetProperty("BodyRef"))!;
        var inFileRows = expectedRead == LuxembourgInFileRightsReadStatus.Observed
            ? new[] { new LuxembourgRightsChannelObservation(Manifestation, profileEvidence, bodyRef,
                reading.GetProperty("LicenceIris").EnumerateArray().Select(value => value.GetString()!).ToArray()) }
            : [];
        // The replay must carry what the adapter's own fold now carries. A refused reading yields
        // no row -- it has no licence -- but the refusal itself is retained, so the replayed channel
        // says "read and refused" rather than "never enumerated". Rebuilding the channel without it
        // would replay a different channel from the one the run produced.
        string[] replayRejected = expectedRead == LuxembourgInFileRightsReadStatus.Observed
            ? []
            : [Manifestation];
        var replayObservations = sparqlIndex.Value.GetProperty("observations").EnumerateArray().Select(row =>
        {
            var objectRef = JsonSerializer.Deserialize<SourceObjectRef>(row.GetProperty("ObjectRef"))!;
            var observed = JsonSerializer.Deserialize<LuxembourgObservedAssertion[]>(row.GetProperty("Assertions"))!;
            var channelOne = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(observed, profileEvidence)
                .Select(channel => new LuxembourgRightsChannelObservation(channel.ManifestationIri,
                    profileEvidence, sparqlRef!, channel.LicenceIris)).ToArray();
            return new LuxembourgResourceObservation(objectRef, profileEvidence, observed, [],
                new LuxembourgSparqlRightsChannelObservations(profileEvidence, sparqlRef!, channelOne),
                new LuxembourgInFileRightsChannelObservations(profileEvidence, inFileRef!,
                    inFileRows.Where(channel => observed.Any(assertion => assertion.SubjectIri == channel.ManifestationIri)).ToArray(), true,
                    replayRejected.Where(iri => observed.Any(assertion => assertion.SubjectIri == iri)).ToArray()));
        }).ToArray();
        var replay = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(result.FamilyOutcomes.Single(outcome => outcome.FamilyKey == "assertions").Proof!,
                replayObservations)));
        var rights = replay.Resources.Single(resource => resource.ObjectRef.PublisherUri == Work).BodyJoin.Candidates.Single().RightsResolution;
        var expectedRights = licenceIri is null ? LuxembourgRightsChannelDisposition.MissingValue : inFileCase switch
        {
            "agreed" or "unicode_item" or "escaped_item" => LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            "conflict" => LuxembourgRightsChannelDisposition.Conflict,
            "missing" => LuxembourgRightsChannelDisposition.MissingValue,
            // The reader examined these two representations and refused them — MalformedXml for
            // one, ManifestationIdentityMismatch for the other, each asserted on its own retained
            // reading elsewhere in this method. They used to land on ChannelEnumerationUnproven,
            // which says this channel never established anything about the manifestation, while its
            // reader had already established exactly why it would not accept it. Named here rather
            // than left to the discard arm, so a future case cannot join them silently.
            "malformed" or "mismatched" =>
                LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            _ => LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven,
        };
        Assert.AreEqual(expectedRights, rights.Disposition);
        var replayResolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(store,
            profile.Snapshot.CompleteEnumerationRef, replayObservations, replay.OrderedEvidenceArtifacts, CancellationToken.None);
        var replayManifest = profile.ReduceScope(replay, replayResolver,
            LuxembourgQueryExecutionAdapter.MintDocumentFetchAddresses(replay)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToScopeManifestFetchAddress()));
        using var replayBytes = new MemoryStream();
        Assert.AreEqual(result.ScopeManifestCanonicalSha256, ScopeManifestCanonicalWriter.Write(replayBytes, replayManifest));
        Assert.AreEqual(finalManifest, Encoding.UTF8.GetString(replayBytes.ToArray()));

        HttpResponseMessage DocumentResponse(HttpRequestMessage request)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(new Uri(item.Replace("http://data.legilux.public.lu/", "https://legilux.public.lu/", StringComparison.Ordinal)).AbsoluteUri,
                request.RequestUri!.AbsoluteUri);
            return Response(request, documentBytes, "application/xml");
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AConsolidationUsesOnlyItsExactOriginalFromTheSameProvenFamilies(bool includeExactOriginal)
    {
        const string parent = "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1";
        const string original = parent + "/jo";
        const string unrelatedOriginal = parent + "/unrelated/jo";
        const string types = "http://data.legilux.public.lu/resource/authority/resource-type/";
        var store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var receipt = await store.CreateAsync("auxiliary original graph fixture"u8.ToArray(),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        var evidence = new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            evidence, evidence, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        (string Subject, string Predicate, string Value)[] assertions =
        [
            (Work, RdfType, Jolux + "Consolidation"),
            (Work, Jolux + "typeDocument", types + "CODE"),
            (Work, Jolux + "isMemberOf", parent),
            (Work, Jolux + "isRealizedBy", Expression),
            (Expression, RdfType, Jolux + "Expression"),
            (Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            (Expression, Jolux + "isEmbodiedBy", Manifestation),
            (Manifestation, RdfType, Jolux + "Manifestation"),
            (Manifestation, Jolux + "userFormat", "http://data.legilux.public.lu/resource/authority/user-format/xml"),
            (Manifestation, Jolux + "isExemplifiedBy", Item),
            // An Act sharing a parent is insufficient when its own exact /jo coordinate is wrong.
            (unrelatedOriginal, RdfType, Jolux + "Act"),
            (unrelatedOriginal, Jolux + "typeDocument", types + "LOI"),
            (unrelatedOriginal, Jolux + "isMemberOf", parent),
        ];
        if (includeExactOriginal)
        {
            assertions = [.. assertions,
                (original, RdfType, Jolux + "Act"),
                (original, Jolux + "typeDocument", types + "LOI"),
                (original, Jolux + "isMemberOf", parent)];
        }
        var intact = LuxembourgQueryExecutionAdapter.BuildResourceObservation(Work,
            assertions.Select(assertion => new LuxembourgObservedAssertion(assertion.Subject, assertion.Predicate,
                LuxembourgAssertionObjectKind.Iri, assertion.Value, "", "", evidence)).ToArray(),
            evidence, profile.ScopeBinding.SourceProfileRef);
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), [intact])));
        Assert.AreEqual(includeExactOriginal ? Lex.V3.Contracts.LuScopeTerminalState.AcceptedCandidate
            : Lex.V3.Contracts.LuScopeTerminalState.TypedQuarantine, resolved.Resources.Single().Dimensions.Body.State,
            "The existing governed resolver must decide the intact graph without weakening its exact-original guard.");

        var subjects = assertions.Select(assertion => assertion.Subject).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var censusPage = LuxembourgAcquisitionTestFixture.RowsJson(subjects);
        var assertionPage = AssertionRows(assertions);
        var body = Encoding.UTF8.GetBytes(LuxembourgInFileRightsReaderTests.Document().Replace(
            "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226/fr/xml", Manifestation, StringComparison.Ordinal));
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(subjects.Length)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(request, censusPage),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 or 14 => Response(request, "User-agent: *\nAllow: /\n"u8.ToArray(), "text/plain"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(request, LuxembourgAcquisitionTestFixture.CountJson(assertions.Length)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(request, assertionPage),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(request, AssertionRows([])),
            15 when includeExactOriginal => Response(request, body, "application/xml"),
            _ => throw new AssertFailedException($"Unexpected request {ordinal}: {request.Method} {request.RequestUri}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, new LuxembourgRepeatedEnumerationExecutor(
            store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler), profile);
        var (censusRequest, censusWitness) = Partition("S", "census");
        var (assertionRequest, assertionWitness) = Partition("A", "assertions");
        var result = await adapter.RunAsync(
            [(censusRequest, censusWitness, null), (assertionRequest, assertionWitness, null)],
            null, "census", "assertions", LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(421), CancellationToken.None);
        Assert.IsNull(result.Refusal, JsonSerializer.Serialize(result.Refusal));
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        Assert.IsNotNull(result.CorpusRecordSet);
        var workRecord = result.CorpusRecordSet.Set.Records.Single(record => record.ObjectRef.PublisherUri == Work);
        Assert.AreEqual(includeExactOriginal ? CorpusBodyRecordKind.Held : CorpusBodyRecordKind.NotHeld, workRecord.Body.Kind,
            "Delivered original-act evidence must reach consolidation qualification; another Act cannot replace it.");

        Assert.IsNotNull(result.ScopeManifestReceipt);
        using var finalManifest = JsonDocument.Parse(await store.ReadAsync(result.ScopeManifestReceipt.Reference, CancellationToken.None));
        var checkedGraph = false;
        foreach (var artifact in finalManifest.RootElement.GetProperty("ordered_evidence_artifacts").EnumerateArray())
        {
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, artifact.GetProperty("sha256").GetString()!, CancellationToken.None);
            if (!Encoding.UTF8.GetString(bytes.Span).Contains("lex-lu-sparql-rights-evidence/1", StringComparison.Ordinal)) continue;
            using var index = JsonDocument.Parse(bytes);
            var workGraph = index.RootElement.GetProperty("observations").EnumerateArray().Single(row =>
                row.GetProperty("ObjectRef").GetProperty("PublisherUri").GetString() == Work);
            var delivered = JsonSerializer.Deserialize<LuxembourgObservedAssertion[]>(workGraph.GetProperty("Assertions"))!;
            Assert.IsFalse(delivered.Any(assertion => assertion.SubjectIri == unrelatedOriginal),
                "Graph collection must not import unrelated original-act assertions.");
            Assert.AreEqual(includeExactOriginal, delivered.Any(assertion => assertion.SubjectIri == original));
            checkedGraph = true;
        }
        Assert.IsTrue(checkedGraph, "The production graph must be openable in retained SPARQL evidence.");
    }

    private static (LuxembourgPartitionRunRequest, BoundMachineRequest) Partition(string setId, string family)
    {
        var (plan, resourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var range = LuxembourgAcquisitionTestFixture.FullRange(family);
        return (new LuxembourgPartitionRunRequest(plan, resourceId, setId, range, renderer),
            plan.BindCount(resourceId, NewUrn(), NewUrn(), setId, LuxembourgQueryPass.Pass1, range, renderer).Request);
    }

    private static string AssertionRows((string Subject, string Predicate, string Value)[] rows)
    {
        var variables = new[] { "subject", "predicate", "object", "object_kind", "datatype_iri", "language_tag",
            "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" };
        var bindings = rows.OrderBy(row => row.Subject, StringComparer.Ordinal)
            .ThenBy(row => row.Predicate, StringComparer.Ordinal).ThenBy(row => row.Value, StringComparer.Ordinal)
            .Select(row =>
            {
                var values = new[] { row.Subject, row.Predicate, row.Value, "iri", "", "",
                    row.Subject, row.Predicate, "iri", row.Value, "", "" };
                return variables.Select((name, index) => (name, term: new
                    { type = index < 3 ? "uri" : "literal", value = values[index] }))
                    .ToDictionary(field => field.name, field => field.term);
            });
        return JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = variables },
            results = new { distinct = false, ordered = true, bindings },
        });
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        content.Headers.ContentLength = bytes.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content };
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
