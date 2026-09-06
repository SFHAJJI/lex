using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LuxembourgLiveAdapterCanary
{
    private const string Start = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
    private const string End = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jp";
    private const string CivilState = "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226";
    private const string CivilOriginal = "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/jo";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [TestMethod]
    public async Task AnActRunsThroughThePublicAdapterWithObservedVocabularyAndSameRunRights()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_ADAPTER_CANARY") != "1")
            Assert.Inconclusive("Set LEX_LU_ADAPTER_CANARY=1 for publisher vocabulary and public-adapter acceptance.");

        await RunCanaryAsync(false);
    }

    [TestMethod]
    public async Task PlainXmlCodeCivilAndItsOriginalRunThroughThePublicAdapterWithSameRunRights()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_XML_ADAPTER_CANARY") != "1")
            Assert.Inconclusive("Set LEX_LU_XML_ADAPTER_CANARY=1 for the declared Code Civil and original-Act scope.");

        await RunCanaryAsync(true);
    }

    private static async Task RunCanaryAsync(bool plainXml)
    {
        var declaredRanges = plainXml
            ? new[] { (Name: "civil-state", Start: CivilState, End: "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251227"),
                (Name: "civil-original", Start: CivilOriginal, End: CivilOriginal + "!") }
            : [(Name: "act-2017", Start, End)];

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "lu-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);
        var measured = new List<object>();
        var observed = new List<ObservedVocabulary>();
        object? provenance = null;
        object? finalResult = null;
        LuxembourgIriVocabularyValue[] missing = [];
        string status = "started";
        string? failure = null;
        try
        {
            // Capture before the first publisher request; dirty source and loaded binaries are
            // separately identified because a commit alone does not identify an uncommitted run.
            provenance = new
            {
                startedUtc = DateTimeOffset.UtcNow,
                head = Git(checkout, "rev-parse", "HEAD"),
                dirtyPaths = Git(checkout, "status", "--porcelain"),
                sourceFiles = Directory.EnumerateFiles(Path.Combine(checkout, "src"), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    .Append(Path.Combine(checkout, "tests/Lex.V3.Ingest.Tests/LuxembourgLiveAdapterCanary.cs"))
                    .Order(StringComparer.Ordinal).Select(path => FileIdentity(checkout, path)).ToArray(),
                assemblies = new[] { typeof(LuxembourgLiveAdapterCanary).Assembly, typeof(LuxembourgQueryExecutionAdapter).Assembly,
                    typeof(LuxembourgQueryPlan).Assembly, typeof(FileSystemCustodyStore).Assembly }
                    .Distinct().Select(assembly => FileIdentity(checkout, assembly.Location)).ToArray(),
            };
            await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(provenance, JsonOptions));
            var scope = await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-declared-canary-scope/1",
                ranges = declaredRanges.Select(range => new { range.Name, range.Start, range.End }).ToArray(),
                vocabulary = "P/T/C whole range; O CC-BY range",
            }, JsonOptions));
            var plan = LuxembourgQueryPlan.CreateDefaultGraph(
                OfficialMachineQuerySourceProfiles.Resolve(OfficialMachineQuerySourceProfileId.LuxembourgSparql).ArtifactRef, scope);
            var planId = NewUrn();
            var queryRenderer = await RendererAsync(store, checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgQueryPlan.cs");
            var documentRenderer = await RendererAsync(store, checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgDocumentFetchPlan.cs");
            var executor = new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System);

            foreach (var family in new[] { "P", "T", "C", "O" })
            {
                var range = family == "O"
                    ? Range("vocabulary-" + family.ToLowerInvariant(), "http://creativecommons.org/licenses/by/4.0/", "http://creativecommons.org/licenses/by/4.1/")
                    : Range("vocabulary-" + family.ToLowerInvariant(), "", "\uffff");
                var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, queryRenderer);
                var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, queryRenderer);
                var outcome = await executor.RunPartitionAsync(request, witness.Request, CancellationToken.None);
                measured.Add(new { family, outcome.ProductRequestCount, outcome.Refusal, outcome.Receipt?.Delivery,
                    retention = outcome.Receipt?.RetainedFloor.ToString() });
                Assert.IsNotNull(outcome.Receipt, $"Vocabulary {family} refused: {JsonSerializer.Serialize(outcome.Refusal)}");
                var proof = AbsenceFamilyEnumerationProof.TryCreate(range.PartitionId, outcome.Receipt.Delivery,
                    outcome.Receipt.RetainedFloor, out var proofRefusal);
                Assert.IsNotNull(proof, $"Vocabulary {family} proof refused: {proofRefusal}");
                var interpretation = plan.CreateDeliveryProfile(planId, family);
                var pages = new List<RepeatedEnumerationResolvedEvidence>();
                var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
                foreach (var page in outcome.Receipt.Delivery.PagesA.Pages.OrderBy(page => page.Ordinal))
                    pages.Add(await glue.ReopenPageEvidenceAsync(page.Evidence, CancellationToken.None));
                var rows = VerifiedRepeatedEnumerationRows.TryOpen(proof, outcome.Receipt.Delivery, interpretation,
                    outcome.Receipt.Delivery.InterpretationProfileRef, outcome.Receipt.Delivery.CountA.HttpEvidenceRef,
                    pages, out var rowRefusal);
                Assert.IsNotNull(rows, $"Vocabulary {family} reopened rows refused: {rowRefusal}");
                var keyOrdinal = interpretation.ProjectionVariables.ToList().IndexOf("key_1");
                Assert.IsTrue(keyOrdinal >= 0);
                foreach (var row in rows)
                {
                    var value = row.Terms[keyOrdinal].Value;
                    Assert.IsNotNull(value, $"Unbound vocabulary {family} key_1");
                    observed.AddRange(Classify(family, value));
                }
            }

            var vocabulary = observed.Where(value => value.Kind is not null)
                .Select(value => new LuxembourgIriVocabularyValue(value.Kind!.Value, value.FullIri)).Distinct().ToArray();
            // Required values are expectations, never a source of observed values.
            missing = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Except(vocabulary).ToArray();
            var vocabularyIndex = await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-live-vocabulary-evidence/1", measured, observed, missing,
                limitation = "Observed publisher values only; no manufactured required vocabulary or production retention.",
            }, JsonOptions));
            var snapshot = new LuxembourgVocabularySnapshot(vocabularyIndex, vocabularyIndex, vocabulary, []);
            var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var profileFailure);
            Assert.IsEmpty(missing, $"Publisher vocabulary lacks required values; retained index {vocabularyIndex.Sha256}");
            Assert.IsNotNull(profile, $"Observed vocabulary refused: {profileFailure?.Code} {profileFailure?.Subject}");

            var families = new List<(LuxembourgPartitionRunRequest, BoundMachineRequest, LuxembourgPartitionChain?)>();
            foreach (var declared in declaredRanges)
            foreach (var family in new[] { "S", "A", "G" })
            {
                var range = Range(declared.Name + "-" + family.ToLowerInvariant(), declared.Start, declared.End);
                var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, queryRenderer);
                var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, queryRenderer);
                families.Add((request, witness.Request, null));
            }
            var adapter = new LuxembourgQueryExecutionAdapter(store, executor, profile);
            var result = plainXml
                ? await adapter.RunScopedAsync(families, declaredRanges.Select(range =>
                    new LuxembourgScopePartitionFamilies(range.Name + "-s", range.Name + "-a", range.Name + "-g")).ToArray(),
                    documentRenderer, CancellationToken.None)
                : await adapter.RunAsync(families, "act-2017-g", "act-2017-s", "act-2017-a",
                    documentRenderer, CancellationToken.None);
            finalResult = new { result.Refusal, result.Completion, result.FamilyOutcomes,
                result.ResourceObservationSubjects, result.ResourceObservationExclusions,
                result.ScopeManifestReceipt, result.ScopeManifestCanonicalSha256, result.CorpusRecordSetRef,
                result.DocumentAcquisitionOutcomesByOrdinal };
            Assert.IsNull(result.Refusal, JsonSerializer.Serialize(result.Refusal));
            Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
            Assert.IsNotNull(result.CorpusRecordSet);
            Assert.IsTrue(result.CorpusRecordSet.Set.Records.Any(record => record.Body.Kind == CorpusBodyRecordKind.Held),
                "The bounded work must have an actually held corpus body.");
            await ReplayRightsAndManifestAsync(store, profile, result,
                declaredRanges.Select(range => range.Name + "-a").ToArray(), plainXml ? CivilState + "/fr/xml" : null);
            status = "passed_bounded_adapter_canary";
        }
        catch (Exception exception)
        {
            status = "failed";
            failure = exception.ToString();
            throw;
        }
        finally
        {
            // Export also on publisher/refusal/assertion failures. The member index lists exact
            // on-disk bytes, even if any content-address verification itself failed.
            var members = new List<object>();
            foreach (var lane in new[] { "nightly-floor-90d", "legal-hold" })
            {
                var directory = Path.Combine(root, lane);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    members.Add(new { lane, claimedSha256 = Path.GetFileName(path), sha256 = digest,
                        byteLength = bytes.Length, matchesContentAddress = Path.GetFileName(path) == digest });
                }
            }
            var index = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-live-adapter-canary-evidence/1", status, failure, provenance, measured, observed, missing,
                finalResult, root, declaredRanges = declaredRanges.Select(range => new { range.Name, range.Start, range.End }).ToArray(),
                completedUtc = DateTimeOffset.UtcNow, members,
                limitations = "Bounded prerequisite only; not whole Luxembourg scope, Stage 1 acceptance, or production retention. FileSystemCustodyStore reports unenforced retention.",
            }, JsonOptions);
            var pathToIndex = Path.Combine(root, "evidence-index.json");
            await File.WriteAllBytesAsync(pathToIndex, index);
            Console.WriteLine($"LU adapter evidence: {pathToIndex}; sha256={Convert.ToHexStringLower(SHA256.HashData(index))}; bytes={index.Length}; status={status}");
            await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }
    }

    private static IEnumerable<ObservedVocabulary> Classify(string family, string value)
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        if (family == "P")
        {
            var kinds = required.Where(item => item.FullIri == value && item.Kind is
                LuxembourgVocabularyKind.AssertionPredicate or LuxembourgVocabularyKind.RelationPredicate)
                .Select(item => item.Kind).Distinct().ToArray();
            return kinds.Length == 0 ? [new(family, null, value, "typed_quarantine_unruled_predicate")]
                : kinds.Select(kind => new ObservedVocabulary(family, kind, value, "governed_predicate_observed"));
        }
        LuxembourgVocabularyKind? category = family == "T" ? LuxembourgVocabularyKind.ResourceClass : null;
        if (family is "C" or "O")
        {
            (string Root, LuxembourgVocabularyKind Kind)[] roots =
            [
                ("http://data.legilux.public.lu/resource/authority/resource-type/", LuxembourgVocabularyKind.TypeDocument),
                ("http://data.legilux.public.lu/resource/authority/user-format/", LuxembourgVocabularyKind.UserFormat),
                ("http://data.legilux.public.lu/resource/authority/statut-version/", LuxembourgVocabularyKind.LegalValue),
                ("http://publications.europa.eu/resource/authority/language/", LuxembourgVocabularyKind.Language),
                ("http://data.legilux.public.lu/resource/authority/license/", LuxembourgVocabularyKind.Licence),
                ("http://creativecommons.org/licenses/", LuxembourgVocabularyKind.Licence),
            ];
            category = roots.Where(root => value.StartsWith(root.Root, StringComparison.Ordinal))
                .Select(root => (LuxembourgVocabularyKind?)root.Kind).SingleOrDefault();
        }
        return [new(family, category, value, category is { } kind && required.Any(item => item.Kind == kind && item.FullIri == value)
            ? "governed_value_observed" : "typed_quarantine_unruled_value")];
    }

    private static async Task ReplayRightsAndManifestAsync(ICustodyStore store,
        VerifiedLuxembourgSourceProfile profile, LuxembourgQueryExecutionResult result,
        IReadOnlyList<string> assertionFamilyKeys, string? expectedManifestation)
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
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("schema", out var schema))
                indexes[schema.GetString()!] = (reference, document.RootElement.Clone());
        }
        var sparql = indexes["lex-lu-sparql-rights-evidence/1"];
        var inFile = indexes["lex-lu-in-file-rights-evidence/1"];
        var acquisitionBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            inFile.Json.GetProperty("acquisitionManifestContentSha256").GetString()!, CancellationToken.None);
        var acquisitionRef = JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("acquisitionManifestRef"))!;
        Assert.AreEqual(acquisitionRef.Sha256, ScopeManifestCanonicalWriter.ComputeManifestSha256(acquisitionBytes.Span));
        var run = profile.Snapshot.ObservationRef;
        Assert.AreEqual(run, JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("runIdentity")),
            "The retained in-file index must identify this exact observation run.");
        var readings = inFile.Json.GetProperty("readings").EnumerateArray().ToArray();
        Assert.IsTrue(readings.Length > 0);
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
            var channelOne = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(assertions, run)
                .Select(value => new LuxembourgRightsChannelObservation(value.ManifestationIri, run, sparql.Ref, value.LicenceIris)).ToArray();
            return new LuxembourgResourceObservation(objectRef, run, assertions, [],
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
            Assert.IsTrue(rights.Length > 0, "Retained in-file declaration must resolve onto this run's proven WEMI graph.");
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

    private sealed record ObservedVocabulary(string Family, LuxembourgVocabularyKind? Kind, string FullIri, string Disposition);
    private static LuxembourgQueryPartitionRange Range(string name, string start, string end) => new(name,
        new LuxembourgQueryCursor(start, "", "", "", "", ""), new LuxembourgQueryCursor(end, "", "", "", "", ""));
    private static async Task<SourceArtifactRef> HoldAsync(ICustodyStore store, byte[] bytes)
    {
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await CustodyRestore.ReadByDigestCheckedAsync(store, receipt.Reference.ContentSha256, CancellationToken.None);
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }
    private static async Task<MachineQueryRendererSource> RendererAsync(ICustodyStore store, string checkout, string path)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(checkout, path));
        return MachineQueryRendererSource.Open(await HoldAsync(store, bytes), bytes);
    }
    private static object FileIdentity(string checkout, string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new { path = Path.GetRelativePath(checkout, path), sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), byteLength = bytes.Length };
    }
    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }
    private static string Git(string checkout, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git failed.");
        return result;
    }
    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
