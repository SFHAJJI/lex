using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Release 2's rights measured over Luxembourg run 15: every held body read by in-file reading /1 (as the acquisition read it)
/// and by reading /2, resolved by resolution /2 against the publisher catalogue's licences, each single-channel body bound to
/// its retained licence page, and admitted under policy /1, policy A and each of the owner's rulings (L2, L1, L1b, with
/// option (a) or (b)), counted by class, in bodies and in the articles content /3 admits. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_LU_RIGHTS2_PROBE=1</c>; <c>LEX_LU_RIGHTS2_PROBE_ROOT</c> names a directory
/// holding S3a's <c>run15-held.jsonl</c> (every held body: its selected WEMI, digest, format and the acquisition's rights
/// disposition) and a <c>probe.json</c>: <c>custody</c> (the run's custody root, read by digest through S2's read-only
/// decorator, so a write throws), <c>base_inputs</c> (the inputs /2 D1 re-derives, whose Luxembourg catalog records the
/// resumption), <c>rights_index</c> (the SPARQL channel's rights evidence index) and <c>retained_derive</c> (derive-c's
/// directory, whose index's <c>work_facts</c> hold run 15's typed assertions, opened read-only). It writes
/// <c>rights2-probe.summary.json</c> and <c>run15-held-release2.jsonl</c> (each held body's row with its Release 2
/// disposition under policy A, which <see cref="LuxembourgIndexNineBuildRun15Probe"/> reads) into the probe root; nothing goes
/// over the network.
/// </para>
/// <para>
/// WHAT IT RUNS, through the production code. The licence-page index (<see cref="LuxembourgSparqlLicencePageIndex"/>) over the
/// rights evidence index, live from the catalog's <c>resume.resumed_at</c>. For each body, in the order of
/// <c>run15-held.jsonl</c>: <see cref="LuxembourgInFileRightsReader.ReadAsync"/> (reading /1, which for a body the acquisition
/// admitted must still read <c>Observed</c>) and <see cref="LuxembourgInFileRightsReader.ReadTwoAsync"/>, then
/// <see cref="LuxembourgRightsChannels.ResolveTwo"/> with a SPARQL row made from the catalogue's licences for the fetched
/// manifestation; a single-channel resolution is bound to its page (<see cref="LuxembourgRightsChannelResolution.WithLicencePage"/>).
/// The final disposition is R1's: the acquisition's own for a body it admitted, once /2 agrees; resolution /2's otherwise.
/// Each policy's admission is <see cref="LuxembourgRightsAdmissionPolicies.Admits(string?, LuxembourgRightsChannelResolution)"/>.
/// Then inventory /4 (the catalogue's applicability dates as its starts) and content /3 over every held body, as R1 runs
/// them, for the articles each body would serve.
/// </para>
/// <para>
/// WHAT IT DOES NOT COVER. R0's own SPARQL channel object (its rows are the same <c>jolux:license</c> assertions, read here from
/// derive-c's work facts, with synthetic evidence references, so the independence and stale rules are not exercised); the
/// corpus, index and mount, which <see cref="LuxembourgIndexNineBuildRun15Probe"/> runs over the policy A dispositions this
/// writes; and the index's states (a text is counted as a body here, a state there).
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgInFileRightsReadingTwoRun15Probe
{
    private const string EnableVariable = "LEX_LU_RIGHTS2_PROBE";
    private const string RootVariable = "LEX_LU_RIGHTS2_PROBE_ROOT";
    private const int ChunkBodies = 32;
    private static readonly string Agreed1 = ContractWire.NameOf(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy);

    /// <summary>The policies measured: /1, A, and each of the owner's rulings over A with option (a) and (b), then all three.</summary>
    private static readonly (string Name, string Sha256)[] Policies =
    [
        ("policy_1", LuxembourgRightsAdmissionPolicies.Policy1Sha256),
        ("policy_a", LuxembourgRightsAdmissionPolicies.PolicyASha256),
        ("a+L2(a)", LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, true, false)),
        ("a+L2(b)", LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, true, true)),
        ("a+L1(a)", LuxembourgRightsAdmissionPolicies.PolicyFor(true, false, false, false)),
        ("a+L1(b)", LuxembourgRightsAdmissionPolicies.PolicyFor(true, false, false, true)),
        ("a+L1b(a)", LuxembourgRightsAdmissionPolicies.PolicyFor(false, true, false, false)),
        ("a+L1b(b)", LuxembourgRightsAdmissionPolicies.PolicyFor(false, true, false, true)),
        ("a+L1+L1b+L2(a)", LuxembourgRightsAdmissionPolicies.PolicyFor(true, true, true, false)),
        ("a+L1+L1b+L2(b)", LuxembourgRightsAdmissionPolicies.PolicyFor(true, true, true, true)),
    ];

    [TestMethod]
    public async Task ReleaseTwoRightsOverRun15AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_LU_RIGHTS2_PROBE=1 and LEX_LU_RIGHTS2_PROBE_ROOT for the read-only run-15 rights measurement.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "probe.json")))!;
        var store = new LuxembourgAknInventoryV4Run15Probe.ReadOnlyCustody(
            FileSystemCustodyStore.WithBrotliCompression((string)settings["custody"]!));
        var clock = Stopwatch.StartNew();
        var phases = new JsonObject();
        void Phase(string name) => phases[name] = Math.Round(clock.Elapsed.TotalSeconds, 1);

        // ---- The catalog's resumption: a licence page is live when the resumed run observed it, not before. ----
        var inputs = await ReadJsonAsync(store, (string)settings["base_inputs"]!);
        var catalog = await ReadJsonAsync(store, (string)inputs["luxembourg"]!["sha256"]!);
        var resumedAt = (string?)catalog["resume"]?["resumed_at"] is { } resumed
            ? DateTimeOffset.Parse(resumed, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime()
            : (DateTimeOffset?)null;

        // ---- The catalogue: run 15's typed jolux:license assertions and dateApplicability facts, as derive-c's index holds them. ----
        var assertions = new List<(string ManifestationIri, string? LicenceIri)>();
        var applicability = new List<(string SubjectIri, string RawLexicalValue)>();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.Combine((string)settings["retained_derive"]!, "luxembourg-index.sqlite3"),
                   Mode = SqliteOpenMode.ReadOnly,
                   Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT predicate, subject_iri, object_kind, object_value, datatype_iri, language_tag FROM work_facts " +
                "WHERE predicate IN ('license', 'dateApplicability')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(0) == "dateApplicability")
                {
                    applicability.Add((reader.GetString(1), reader.GetString(3)));
                    continue;
                }

                var exact = reader.GetString(2) == "iri" && reader.GetString(4).Length == 0 && reader.GetString(5).Length == 0;
                assertions.Add((reader.GetString(1), exact ? reader.GetString(3) : null));
            }
        }

        SqliteConnection.ClearAllPools();
        var catalogue = LuxembourgRightsCatalogue.From(assertions);
        var licencesOf = assertions
            .GroupBy(static assertion => assertion.ManifestationIri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (Licences: group.Where(static row => row.LicenceIri is not null).Select(static row => row.LicenceIri!)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    Unrepresentable: group.Count(static row => row.LicenceIri is null)),
                StringComparer.Ordinal);
        assertions.Clear();
        Phase("catalogue");

        // ---- The licence-page index over the SPARQL channel's retained pages. ----
        var pages = await LuxembourgSparqlLicencePageIndex.BuildAsync(store, (string)settings["rights_index"]!, resumedAt, CancellationToken.None);
        Phase("licence_pages");

        // One run identity for both channels (the rights' bound identity), synthetic and distinct evidence references.
        var run = Ref("run");
        var enumeration = Ref("sparql-enumeration");
        var sparqlRow = Ref("sparql-row");
        var inFileIndex = Ref("in-file-evidence-2");

        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new SortedDictionary<string, JsonArray>(StringComparer.Ordinal);
        var failures = new List<string>();
        void Count(string key, string? example = null, int by = 1)
        {
            tally[key] = tally.GetValueOrDefault(key) + by;
            if (example is not null)
            {
                if (!examples.TryGetValue(key, out var list))
                {
                    examples[key] = list = [];
                }

                if (list.Count < 3)
                {
                    list.Add(example);
                }
            }
        }

        var bodies = new List<Body>();
        var rows = new List<JsonObject>();
        var template = await new EuAcquisitionTestFixture.EuInMemoryCustodyStore().CreateAsync(
            "template"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var custodyPolicy = template.PolicyEvidence;
        var held = new List<(LuxembourgHeldPopulationBuilder.Member Member, DurableBlobWriteReceipt Receipt)>();
        foreach (var line in File.ReadLines(Path.Combine(root, "run15-held.jsonl")))
        {
            var row = JsonNode.Parse(line)!.AsObject();
            var rootIri = (string)row["root_iri"]!;
            var manifestation = (string)row["manifestation_iri"]!;
            var digest = (string)row["body_sha256"]!;
            var acquired = (string)row["rights_disposition"]!;
            var format = LuxembourgHeldPopulationBuilder.Token((string)row["format"]!);
            var kind = LuxembourgEliState.KindOf(rootIri);
            var bodyRef = new SourceArtifactRef(
                ContentDerivedIdentity.DeriveUuidUrn("lex-lu-in-file-body-digest/1", Encoding.UTF8.GetBytes(digest)), digest);

            var one = await LuxembourgInFileRightsReader.ReadAsync(store, bodyRef, manifestation, run, format, CancellationToken.None);
            var two = await LuxembourgInFileRightsReader.ReadTwoAsync(store, bodyRef, manifestation, format, CancellationToken.None);
            var (licences, unrepresentable) = licencesOf.GetValueOrDefault(manifestation, ([], 0));
            // The acquisition's SPARQL channel holds a row for every held body's manifestation (R1 refuses one without: no
            // run-15 body is channel_enumeration_unproven), empty when the catalogue states no licence for it.
            var sparql = new LuxembourgSparqlRightsChannelObservations(
                run, enumeration, [new LuxembourgRightsChannelObservation(manifestation, run, sparqlRow, licences, unrepresentable)]);
            var resolutionTwo = LuxembourgRightsChannels.ResolveTwo(manifestation, run, sparql, run, inFileIndex, two, catalogue);
            var page = "none";
            if (LuxembourgRightsChannelResolution.IsSingleChannel(resolutionTwo.Disposition))
            {
                if (pages.PageOf(manifestation) is { } found && found.LicenceIris.SequenceEqual(licences, StringComparer.Ordinal))
                {
                    resolutionTwo = resolutionTwo.WithLicencePage(found);
                    page = found.ObservedLive ? "live" : "replayed";
                }
            }

            var resolved = ContractWire.NameOf(resolutionTwo.Disposition);
            var keepsAdmission = acquired == Agreed1 && resolutionTwo.Disposition == LuxembourgRightsChannelDisposition.AgreedCcByRederived;
            var final = keepsAdmission ? Agreed1 : resolved;
            if (acquired == Agreed1 && !keepsAdmission)
            {
                failures.Add($"{rootIri}: the acquisition admitted it and resolution /2 says {resolved}");
            }

            if (acquired == Agreed1 && one.Status != LuxembourgInFileRightsReadStatus.Observed)
            {
                failures.Add($"{rootIri}: the acquisition admitted it and reading /1 now reads {one.Status}");
            }

            var admittedBy = Policies.Select(policy => keepsAdmission
                ? LuxembourgRightsAdmissionPolicies.Admits(policy.Sha256, LuxembourgRightsChannelDisposition.AgreedSameRunCcBy)
                : LuxembourgRightsAdmissionPolicies.Admits(policy.Sha256, resolutionTwo)).ToArray();
            Count($"bodies|{kind}");
            Count($"reading_1|{kind}|{one.Status}");
            Count($"reading_2|{kind}|{ContractWire.NameOf(two.Status)}", rootIri);
            Count($"acquired|{kind}|{acquired}");
            Count($"resolution_2|{kind}|{resolved}", rootIri);
            Count($"transition|{kind}|{acquired} -> {final}", acquired == final ? null : rootIri);
            if (LuxembourgRightsChannelResolution.IsSingleChannel(resolutionTwo.Disposition))
            {
                Count($"single_channel_licence_page|{kind}|{resolved}|{page}", page == "none" ? rootIri : null);
            }

            if (admittedBy[1] && !admittedBy[0])
            {
                Count($"newly_admitted_by_policy_a|{kind}|reading_1 {one.Status}, reading_2 {ContractWire.NameOf(two.Status)}, "
                    + $"{two.NamedIdentities.Count} named, frbr "
                    + $"{(two.FrbrManifestationIdentities.Contains(manifestation, StringComparer.Ordinal) ? "names it" : "differs")}",
                    rootIri);
            }

            bodies.Add(new Body(rootIri, kind, admittedBy));
            row["rights_disposition"] = final;
            rows.Add(row);
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, (long)row["body_byte_length"]!, CustodyClass.NightlyFloor90d);
            held.Add((new LuxembourgHeldPopulationBuilder.Member(
                rootIri,
                [],
                format,
                (int)row["source_ordinal"]!,
                (string)row["expression_iri"]!,
                manifestation,
                (string)row["item_iri"]!,
                (string)row["language_iri"]!), new DurableBlobWriteReceipt(
                    template.Schema, reference,
                    new CustodyPolicyEvidence(custodyPolicy.Schema, reference, custodyPolicy.VerificationProfile, custodyPolicy.PolicyKey,
                        custodyPolicy.Protection, custodyPolicy.ObservedAt, custodyPolicy.ProtectedUntil))));
        }

        File.WriteAllLines(Path.Combine(root, "run15-held-release2.jsonl"), rows.Select(static row => row.ToJsonString()), new UTF8Encoding(false));
        rows.Clear();
        Phase("rights");

        // ---- Inventory /4 and content /3 over every held body, as R1 runs them: the articles each body would serve. ----
        var population = LuxembourgHeldPopulationBuilder.Create(held);
        held.Clear();
        var inventory = await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(
            population, LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(applicability), CancellationToken.None);
        Phase("inventory_4");
        var articles = new Dictionary<string, int>(StringComparer.Ordinal);
        var refusedState = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in inventory.Outcomes)
        {
            if (outcome.Disposition == LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState)
            {
                refusedState.Add(outcome.Input.CorpusRecord.ObjectRef.PublisherUri);
            }
        }

        var ordered = inventory.Outcomes.ToArray();
        for (var start = 0; start < ordered.Length; start += ChunkBodies)
        {
            var part = new LuxembourgAknArticleInventoryPopulation(
                population, ordered.Skip(start).Take(ChunkBodies).ToArray(), LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
            var three = await new LuxembourgAknLegalContentProfile3Producer(store).RunAsync(part, CancellationToken.None);
            foreach (var outcome in three.Outcomes.Where(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted))
            {
                var uri = outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri;
                articles[uri] = articles.GetValueOrDefault(uri) + 1;
            }
        }

        Phase("content_3");

        // ---- Each policy: bodies admitted, bodies with text, articles; and what it adds over policy A. ----
        var byPolicy = new JsonObject();
        for (var index = 0; index < Policies.Length; index++)
        {
            var admitted = bodies.Where(body => body.AdmittedBy[index]).ToArray();
            var overA = bodies.Where(body => body.AdmittedBy[index] && !body.AdmittedBy[1]).ToArray();
            byPolicy[Policies[index].Name] = new JsonObject
            {
                ["sha256"] = Policies[index].Sha256,
                ["bodies_admitted"] = Counted(admitted.Select(static body => body.Kind)),
                ["bodies_with_text"] = Counted(admitted.Where(body => articles.GetValueOrDefault(body.Root) > 0).Select(static body => body.Kind)),
                ["articles"] = Summed(admitted.Select(body => (body.Kind, articles.GetValueOrDefault(body.Root)))),
                ["refused_as_misfiled"] = admitted.Count(body => refusedState.Contains(body.Root)),
                ["over_policy_a"] = new JsonObject
                {
                    ["bodies_admitted"] = Counted(overA.Select(static body => body.Kind)),
                    ["bodies_with_text"] = Counted(overA.Where(body => articles.GetValueOrDefault(body.Root) > 0).Select(static body => body.Kind)),
                    ["articles"] = Summed(overA.Select(body => (body.Kind, articles.GetValueOrDefault(body.Root)))),
                    ["refused_as_misfiled"] = overA.Count(body => refusedState.Contains(body.Root)),
                },
            };
        }

        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-lu-rights2-run15-probe/2",
            ["reading_2_profile_sha256"] = LuxembourgInFileRightsReader.ReadingTwoProfileSha256,
            ["resolution_2_profile_sha256"] = LuxembourgRightsChannels.ResolutionTwoProfileSha256,
            ["resumed_at"] = resumedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["catalogue_manifestations"] = catalogue.Count,
            ["licence_pages"] = new JsonObject
            {
                ["sha256"] = pages.Sha256,
                ["pages"] = pages.PageCount,
                ["licence_rows"] = pages.LicenceRows,
                ["manifestations"] = pages.Manifestations,
                ["manifestations_without_one_page_value"] = pages.DisagreeingManifestations,
            },
            ["bodies"] = bodies.Count,
            ["policies"] = byPolicy,
            ["counts"] = new JsonObject(tally.Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["examples"] = new JsonObject(examples.Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["failures"] = new JsonArray(failures.Take(50).Select(static value => (JsonNode?)value).ToArray()),
            ["failure_count"] = failures.Count,
            ["phases_seconds"] = phases,
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var text = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "rights2-probe.summary.json"), text, new UTF8Encoding(false));
        Console.WriteLine(text);
        Assert.IsEmpty(failures, "Release 2's rights refuse:\n" + string.Join("\n", failures.Take(20)));
    }

    private sealed record Body(string Root, string Kind, bool[] AdmittedBy);

    private static JsonObject Counted(IEnumerable<string> values) =>
        new(values.GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));

    private static JsonObject Summed(IEnumerable<(string Kind, int Count)> values) =>
        new(values.GroupBy(static value => value.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Sum(static value => value.Count))));

    private static async Task<JsonNode> ReadJsonAsync(ICustodyStore store, string sha256) =>
        JsonNode.Parse((await CustodyRestore.ReadByDigestCheckedAsync(store, sha256, CancellationToken.None)).Span)!;

    private static SourceArtifactRef Ref(string name)
    {
        var bytes = Encoding.UTF8.GetBytes("lex-v3-lu-rights2-run15-probe/" + name);
        return new SourceArtifactRef(ContentDerivedIdentity.DeriveUuidUrn("lex-v3-lu-rights2-run15-probe", bytes), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }
}
