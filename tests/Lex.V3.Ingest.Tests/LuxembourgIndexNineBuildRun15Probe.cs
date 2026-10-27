using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1's index stage, run locally: index /9's whole build over Luxembourg run 15 under Release 1's rules, from the projection
/// of every row to the written file read back by the reader, without the R0 replay a derive needs 28 GB for. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_LU_INDEX9_BUILD_PROBE=1</c>; <c>LEX_LU_INDEX9_BUILD_PROBE_ROOT</c> names a
/// directory holding S3a's <c>run15-held.jsonl</c> (every held body: its selected WEMI, digest and rights) and a
/// <c>probe.json</c>: <c>custody</c> (the run's custody root, read through S2's read-only decorator, so a write throws),
/// <c>base_inputs</c> (the digest of the offline inputs /2 D1 re-derives), <c>retained_derive</c> (derive-c's directory: its
/// corpus <c>lex-corpus-6.json</c> and its index's <c>work_facts</c>, opened read-only), <c>built_at</c> and
/// <c>derived_at</c>. It writes its summary into the probe root and its index files into the temporary folder, and removes
/// them; nothing goes over the network.
/// </para>
/// <para>
/// RELEASE 2. With <c>held</c> naming <c>run15-held-release2.jsonl</c> (what <see cref="LuxembourgInFileRightsReadingTwoRun15Probe"/>
/// writes: each held body's disposition under reading /2 and resolution /2) and <c>admission_policy</c> <c>A</c>, a body is
/// acquired when policy A admits its disposition, and each corpus member whose disposition the file changes is re-bound as R1
/// re-binds it (resolution /2, binding /2, the policy's outcome; derive-c's evidence references stand in for the /2 evidence
/// index), so the index builds with the consolidations' text. Without them it runs Release 1, as before.
/// </para>
/// <para>
/// WHAT IT RUNS, from real data, through the production code:
/// </para>
/// <list type="bullet">
/// <item>the corpus record set the acquisition retained (inputs /2, its Luxembourg catalog, its query checkpoint, then
/// <see cref="CorpusRecordSetReader"/>): every record's publisher URI, object reference digest and body disposition;</item>
/// <item>inventory /4 over every held body (the catalogue's <c>dateApplicability</c> facts as its starts) and content /3 over
/// what it inventoried, as <see cref="LuxembourgRederivation"/> runs them (content /3 in chunks of bodies, so the token
/// streams of 300,000 articles are never held at once; an article is kept only for an acquired body, the only ones the
/// projection reads);</item>
/// <item>the corpus's Luxembourg members from derive-c's corpus, each held member's legal-content stage-3 outcomes replaced by
/// content /3's, as the corpus builder lists them (<see cref="LexCorpus6Builder.AknDisposition"/>, the corpus's order); the
/// typed assertions as derive-c's <c>work_facts</c> rows (the table is every distinct assertion row);</item>
/// <item><see cref="LuxembourgIndexBuilder.TryProjectRows"/> (the build's own projection), then
/// <see cref="LuxembourgIndexBuilder.WriteFile"/> (the exact DDL; every row the database refuses is collected and named, so
/// one pass finds every constraint a row breaks), then the reader's file door
/// (<see cref="LuxembourgIndexReader.OpenAndVerifyFileAsync"/>: every validation the reader runs) and its check of the log's
/// source bodies against the corpus; then a second write of the same rows, whose file must be the same bytes, as the build's
/// double build requires.</item>
/// </list>
/// <para>
/// WHAT IT DOES NOT COVER. The R0 replay and R1 as the derive runs them (its population is S3a's synthetic one over the real
/// bodies, so the content outcomes' semantic identities, which hash the population and the object's canonical key, are not
/// the derive's: their uniqueness and the stage-3 check are run, not reproduced, and the members' stage-3 JSON carries these
/// identities); the corpus build under R1 (derive-c's members stand in, with R1's content outcomes; their PDF outcomes are
/// derive-c's); the build-time check against the corpus; the typed assertions' original order (the work facts are sorted, so
/// rows that tie on the title table's order may come in another order); the EU index, the mount writer and the mount's
/// verification.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgIndexNineBuildRun15Probe
{
    private const string EnableVariable = "LEX_LU_INDEX9_BUILD_PROBE";
    private const string RootVariable = "LEX_LU_INDEX9_BUILD_PROBE_ROOT";
    private const int ChunkBodies = 32;

    [TestMethod]
    public async Task IndexNinesBuildOverRun15IsWrittenAndVerified()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                "Set LEX_LU_INDEX9_BUILD_PROBE=1 and LEX_LU_INDEX9_BUILD_PROBE_ROOT for the read-only run-15 index build.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root), RootVariable + " names the probe inputs.");
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "probe.json")))!;
        var store = new LuxembourgAknInventoryV4Run15Probe.ReadOnlyCustody(
            FileSystemCustodyStore.WithBrotliCompression((string)settings["custody"]!));
        var retained = (string)settings["retained_derive"]!;
        var builtAtText = (string)settings["built_at"]!;
        var derivedAtText = (string)settings["derived_at"]!;
        var clock = Stopwatch.StartNew();
        var phases = new JsonObject();
        void Phase(string name) => phases[name] = Math.Round(clock.Elapsed.TotalSeconds, 1);
        var summary = new JsonObject { ["schema"] = "lex-v3-lu-index9-build-run15-probe/1", ["phases_seconds"] = phases };

        // ---- The acquisition's corpus record set, by its retained chain: inputs /2, the Luxembourg catalog, the query. ----
        var inputs = await ReadJsonAsync(store, (string)settings["base_inputs"]!);
        var catalog = await ReadJsonAsync(store, (string)inputs["luxembourg"]!["sha256"]!);
        var parts = (catalog["population"] ?? catalog)["parts"]!;
        var query = await ReadJsonAsync(store, (string)parts["query"]!["sha256"]!);
        var setRef = new SourceArtifactRef((string)query["corpus"]!["resource_id"]!, (string)query["corpus"]!["sha256"]!);
        var receipt = await ReceiptAsync(store, (string)query["corpus_content_sha256"]!);
        var read = await new CorpusRecordSetReader(store).ReadAsync(receipt, setRef, CancellationToken.None);
        Assert.IsNotNull(read.VerifiedSet, read.Refusal?.Detail);
        var records = read.VerifiedSet.Set.Records
            .Select(static record => new LuxembourgIndexBuilder.ProjectionRecord(
                record.ObjectRef.PublisherUri,
                Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter.ComputeObjectRefSha256(record.ObjectRef),
                ContractWire.NameOf(record.BodyDisposition)))
            .ToArray();
        var heldObjectRefByUri = read.VerifiedSet.Set.Records
            .Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held)
            .ToDictionary(
                static record => record.ObjectRef.PublisherUri,
                static record => Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter.ComputeObjectRefSha256(record.ObjectRef),
                StringComparer.Ordinal);
        read = null!;
        Phase("records");

        // ---- derive-c's corpus (R0's members) and its index's work facts (the run's typed assertions). ----
        var corpusBytes = await File.ReadAllBytesAsync(Path.Combine(retained, "lex-corpus-6.json"));
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        corpusBytes = null!;
        var predicates = Enum.GetValues<LuxembourgAssertionPredicate>()
            .ToDictionary(static value => ContractWire.NameOf(value), StringComparer.Ordinal);
        var assertions = new List<LuxembourgIndexBuilder.ProjectionAssertion>();
        var catalogueFacts = new List<(string SubjectIri, string RawLexicalValue)>();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.Combine(retained, "luxembourg-index.sqlite3"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT subject_iri,predicate,fact_kind,object_kind,object_value,datatype_iri,language_tag,evidence_sha256 FROM work_facts";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var fact = new LuxembourgIndexBuilder.WorkFactRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7));
                var predicate = predicates[fact.Predicate];
                assertions.Add(new(predicate, fact));
                if (predicate == LuxembourgAssertionPredicate.DateApplicability)
                {
                    catalogueFacts.Add((fact.SubjectIri, fact.ObjectValue));
                }
            }
        }
        SqliteConnection.ClearAllPools();
        Phase("corpus_and_facts");

        // ---- The held population over the run's custody, inventory /4 and content /3, as R1 runs them. ----
        var template = await new EuAcquisitionTestFixture.EuInMemoryCustodyStore().CreateAsync(
            "template"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var policy = template.PolicyEvidence;
        var held = new List<(LuxembourgHeldPopulationBuilder.Member Member, DurableBlobWriteReceipt Receipt)>();
        var rightsByRoot = new Dictionary<string, string>(StringComparer.Ordinal);
        var rightsByObjectRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var mismatchedObjectRefs = 0;
        // Release 1 reads the acquisition's rights (run15-held.jsonl, policy /1); Release 2 reads the dispositions
        // LuxembourgInFileRightsReadingTwoRun15Probe wrote (run15-held-release2.jsonl) under policy A.
        var heldFile = (string?)settings["held"] ?? "run15-held.jsonl";
        var admissionPolicy = (string?)settings["admission_policy"] switch
        {
            null or "1" => LuxembourgRightsAdmissionPolicies.Policy1Sha256,
            "A" => LuxembourgRightsAdmissionPolicies.PolicyASha256,
            var other => throw new AssertFailedException($"admission_policy {other} is not a registered policy's name"),
        };
        summary["held"] = heldFile;
        summary["admission_policy"] = admissionPolicy;
        foreach (var line in File.ReadLines(Path.Combine(root, heldFile)))
        {
            var row = JsonNode.Parse(line)!;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, (string)row["body_sha256"]!, (long)row["body_byte_length"]!, CustodyClass.NightlyFloor90d);
            held.Add((new LuxembourgHeldPopulationBuilder.Member(
                (string)row["root_iri"]!,
                [],
                LuxembourgHeldPopulationBuilder.Token((string)row["format"]!),
                (int)row["source_ordinal"]!,
                (string)row["expression_iri"]!,
                (string)row["manifestation_iri"]!,
                (string)row["item_iri"]!,
                (string)row["language_iri"]!), new DurableBlobWriteReceipt(
                    template.Schema, reference,
                    new CustodyPolicyEvidence(policy.Schema, reference, policy.VerificationProfile, policy.PolicyKey,
                        policy.Protection, policy.ObservedAt, policy.ProtectedUntil))));
            rightsByRoot[(string)row["root_iri"]!] = (string)row["rights_disposition"]!;
            rightsByObjectRef[(string)row["object_ref_sha256"]!] = (string)row["rights_disposition"]!;
            if (!heldObjectRefByUri.TryGetValue((string)row["root_iri"]!, out var objectRef) ||
                !string.Equals(objectRef, (string)row["object_ref_sha256"]!, StringComparison.Ordinal))
            {
                mismatchedObjectRefs++;
            }
        }
        Assert.AreEqual(0, mismatchedObjectRefs, "every held body is a held record of the retained corpus record set");
        var population = LuxembourgHeldPopulationBuilder.Create(held);
        held.Clear();
        var inventory = await new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(
            population, LuxembourgAknArticleInventoryProducerV4.CatalogueStarts(catalogueFacts), CancellationToken.None);
        Phase("inventory_4");

        string ObjectRefOf(LuxembourgAknArticleInventoryOutcome outcome) =>
            heldObjectRefByUri[outcome.Input.CorpusRecord.ObjectRef.PublisherUri];
        var inventoryInputs = inventory.Outcomes
            .Select(outcome => new LuxembourgIndexBuilder.ProjectionInventory(ObjectRefOf(outcome), outcome))
            .ToArray();
        var content = new List<LuxembourgIndexBuilder.ProjectionContent>();
        var ordered = inventory.Outcomes.ToArray();
        for (var start = 0; start < ordered.Length; start += ChunkBodies)
        {
            var part = new LuxembourgAknArticleInventoryPopulation(
                population, ordered.Skip(start).Take(ChunkBodies).ToArray(), LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256);
            var three = await new LuxembourgAknLegalContentProfile3Producer(store).RunAsync(part, CancellationToken.None);
            foreach (var outcome in three.Outcomes)
            {
                var acquired = LuxembourgRightsAdmissionPolicies.Admits(
                    admissionPolicy, Disposition(rightsByRoot[outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri]));
                content.Add(new(
                    ObjectRefOf(outcome.SourceInventoryOutcome),
                    outcome.SemanticIdentitySha256,
                    outcome.Disposition,
                    acquired ? outcome.Article : null,
                    outcome.SourceInventoryOutcome.Inventory));
            }
        }
        Phase("content_3");

        // ---- The corpus's Luxembourg members under R1: each held member's legal-content outcomes are content /3's. ----
        var contentByObject = content
            .GroupBy(static outcome => outcome.ObjectRefSha256, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static outcome => new LexCorpus6Stage3Outcome(
                    LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent,
                    outcome.SemanticIdentitySha256,
                    LexCorpus6Builder.AknDisposition(outcome.Disposition))).ToArray(),
                StringComparer.Ordinal);
        var rebound = 0;
        var members = corpus.Set.Members
            .Where(static member => member.Publisher == PublisherId.LuLegilux)
            .Select(member => member.Stage3Outcomes.Count == 0 && !contentByObject.ContainsKey(member.ObjectRefSha256)
                ? member
                : member with
                {
                    Stage3Outcomes = member.Stage3Outcomes
                        .Where(static outcome => outcome.Domain != LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent)
                        .Concat(contentByObject.GetValueOrDefault(member.ObjectRefSha256) ?? [])
                        .Order(Comparer<LexCorpus6Stage3Outcome>.Create(LexCorpus6Member.CompareStage3Outcomes))
                        .ToArray(),
                })
            .Select(member =>
            {
                // A held member whose disposition the held file changes is re-bound as R1 re-binds it (resolution /2, binding
                // /2), its outcome the policy's; derive-c's evidence references stand in for the /2 evidence index.
                if (member.LuxembourgRights is not { } rights ||
                    !rightsByObjectRef.TryGetValue(member.ObjectRefSha256, out var wire) ||
                    wire == ContractWire.NameOf(rights.Disposition))
                {
                    return member;
                }

                var disposition = Disposition(wire);
                var admitted = LuxembourgRightsAdmissionPolicies.Admits(admissionPolicy, disposition);
                var rule = LuxembourgRightsChannels.ResolutionTwoProfileSha256;
                rebound++;
                return (member with
                {
                    Outcome = admitted ? LexCorpus6OutcomeKind.Acquired : LexCorpus6OutcomeKind.RightsWithheld,
                    LuxembourgRights = rights with
                    {
                        Disposition = disposition,
                        // Binding /2 names the re-derivation record, which a derive makes and this probe does not: a
                        // stand-in digest, never verified here (the probe builds no corpus).
                        BindingSha256 = LexCorpus6LuxembourgRights.ComputeBindingSha256(
                            member.RunIdentity, rights.BoundRunIdentity, rights.SelectedWemi.IdentitySha256, rule,
                            LuxembourgInFileRightsReader.ReadingTwoProfileSha256, admissionPolicy, new string('0', 64)),
                        ResolutionRule = rule,
                    },
                    Gaps = admitted ? [] : [LuxembourgRightsChannelResolution.ReasonCodeOf(disposition)],
                }).Validate();
            })
            .ToArray();
        contentByObject.Clear();
        summary["inputs"] = new JsonObject
        {
            ["records"] = records.Length,
            ["members"] = members.Length,
            ["members_rebound"] = rebound,
            ["members_acquired"] = members.Count(static member => member.Outcome == LexCorpus6OutcomeKind.Acquired),
            ["typed_assertion_rows"] = assertions.Count,
            ["held_bodies"] = population.Inputs.Count,
            ["inventory_outcomes"] = Counted(inventory.Outcomes.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
            ["content_outcomes"] = Counted(content.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
        };

        // ---- The build's own projection. ----
        var projected = LuxembourgIndexBuilder.TryProjectRowsCatching(
            new LuxembourgIndexBuilder.ProjectionInputs(members, records, content, inventoryInputs, assertions),
            builtAtText[..10],
            out var rows,
            out var refusal,
            out var detail);
        content.Clear();
        assertions.Clear();
        Phase("projection");
        summary["projection"] = projected
            ? new JsonObject
            {
                ["refusal"] = "none",
                ["members"] = rows.Members.Length,
                ["articles"] = rows.Articles.Length,
                ["articles_with_a_date"] = rows.Articles.Count(static article => article.ApplicabilityDate is not null),
                ["states"] = rows.States.Length,
                ["held_states"] = rows.States.Count(static state => state.TextStatus == LuxembourgIndexBuilder.TextHeld),
                ["held_states_by_kind"] = Counted(rows.States.Where(static state => state.TextStatus == LuxembourgIndexBuilder.TextHeld)
                    .Select(static state => KindOf(state.ExpressionIri))),
                ["articles_by_kind"] = Counted(rows.Articles.Select(static article => KindOf(article.ExpressionIri))),
                ["intervals"] = rows.Intervals.Length,
                ["work_titles"] = rows.WorkTitles.Length,
                ["work_titles_by_kind"] = Counted(rows.WorkTitles.Select(static title => KindOf(title.ExpressionIri))),
                ["work_titles_with_a_document_date_by_kind"] = Counted(rows.WorkTitles
                    .Where(static title => title.DocumentDate is not null).Select(static title => KindOf(title.ExpressionIri))),
                ["work_facts"] = rows.WorkFacts.Length,
            }
            : new JsonObject { ["refusal"] = ContractWire.NameOf(refusal), ["detail"] = detail };

        var failures = new List<string>();
        if (!projected)
        {
            failures.Add($"projection: {ContractWire.NameOf(refusal)}: {detail}");
        }
        else
        {
            // ---- The writer, every row the database refuses named, then the reader's file door and the log's source bodies. ----
            var bodies = LuxembourgIndexBuilder.BodiesByObjectRef(corpus);
            var refused = new List<(string Table, int Index, SqliteException Exception)>();
            var first = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index9-probe-{Guid.NewGuid():N}.sqlite");
            var second = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index9-probe-{Guid.NewGuid():N}.sqlite");
            try
            {
                var written = LuxembourgIndexBuilder.WriteFile(
                    first, corpus.ArtifactRef, rows.Members, rows.Articles, rows.States, rows.Intervals, rows.WorkTitles, rows.WorkFacts,
                    bodies, null, builtAtText, derivedAtText, out var writeRefusal, out var writeDetail,
                    (table, index, exception) => refused.Add((table, index, exception)));
                Phase("write");
                summary["refused_rows"] = RefusedRows(refused, rows);
                foreach (var group in refused.GroupBy(static value => (value.Table, value.Exception.Message)))
                {
                    failures.Add($"{group.Key.Table}: {group.Count()} rows: {group.Key.Message}");
                }
                if (written is null)
                {
                    failures.Add($"write: {ContractWire.NameOf(writeRefusal)}: {writeDetail}");
                    summary["write"] = new JsonObject { ["refusal"] = ContractWire.NameOf(writeRefusal), ["detail"] = writeDetail };
                }
                else
                {
                    summary["write"] = new JsonObject
                    {
                        ["refusal"] = "none",
                        ["bytes"] = written.ByteLength,
                        ["index_sha256"] = written.IndexRef.Sha256,
                        ["capability_cells"] = written.CapabilityManifest.Cells.Count,
                    };
                    if (refused.Count == 0)
                    {
                        try
                        {
                            using var verified = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                                first, written.CapabilityManifestBytes, CancellationToken.None);
                            verified.VerifyEventLogSources(corpus);
                            summary["reader"] = new JsonObject
                            {
                                ["verified"] = true,
                                ["articles"] = verified.ArticleCount,
                            };
                        }
                        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or ArgumentException)
                        {
                            failures.Add("reader: " + exception.Message);
                            summary["reader"] = new JsonObject { ["verified"] = false, ["detail"] = exception.Message };
                        }
                        Phase("reader");

                        // The first file goes before the second is written: one index on disk at a time (this PC's free
                        // disk), compared by digest and manifest bytes, as the build's double build compares them.
                        SqliteConnection.ClearAllPools();
                        LuxembourgIndexBuilder.DeleteDatabase(first);
                        var again = LuxembourgIndexBuilder.WriteFile(
                            second, corpus.ArtifactRef, rows.Members, rows.Articles, rows.States, rows.Intervals, rows.WorkTitles,
                            rows.WorkFacts, bodies, null, builtAtText, derivedAtText, out _, out var againDetail);
                        var same = again is not null && again.IndexRef == written.IndexRef &&
                            again.CapabilityManifestBytes.Span.SequenceEqual(written.CapabilityManifestBytes.Span);
                        summary["second_build_same_bytes"] = same;
                        if (!same)
                        {
                            failures.Add("the second build's file differs: " + (againDetail ?? again?.IndexRef.Sha256));
                        }
                        Phase("second_write");
                    }
                }
            }
            finally
            {
                LuxembourgIndexBuilder.DeleteDatabase(first);
                LuxembourgIndexBuilder.DeleteDatabase(second);
            }
        }

        summary["failures"] = new JsonArray(failures.Select(static value => (JsonNode?)value).ToArray());
        summary["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
        var text = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "index9-build-probe.summary.json"), text, new UTF8Encoding(false));
        Console.WriteLine(text);
        Assert.IsEmpty(failures, "the index stage refuses:\n" + string.Join("\n", failures));
    }

    /// <summary>Every refused row by table and message, with the first rows of each and, for a key, the other rows that share it.</summary>
    private static JsonObject RefusedRows(
        IReadOnlyList<(string Table, int Index, SqliteException Exception)> refused, LuxembourgIndexBuilder.ProjectedRows rows)
    {
        var result = new JsonObject();
        foreach (var group in refused.GroupBy(static value => $"{value.Table}: {value.Exception.Message}", StringComparer.Ordinal))
        {
            var examples = new JsonArray();
            foreach (var (table, index, _) in group.Take(12))
            {
                examples.Add((JsonNode?)Describe(table, index, rows));
            }
            var entry = new JsonObject { ["rows"] = group.Count(), ["examples"] = examples };
            if (group.First().Table == "work_titles")
            {
                // By the kind of the title's expression (its legal resource's), and how many keys and expressions they make.
                var titles = group.Select(value => rows.WorkTitles[value.Index]).ToArray();
                entry["by_kind"] = Counted(titles.Select(static title => KindOf(title.ExpressionIri)));
                entry["keys"] = titles.Select(static title => (title.WorkIdentifier, title.ExpressionIri, title.Language, title.Title,
                    title.TitleKind, title.EvidenceSha256)).Distinct().Count();
                entry["expressions"] = titles.Select(static title => title.ExpressionIri).Distinct(StringComparer.Ordinal).Count();
                entry["works"] = titles.Select(static title => title.WorkIdentifier).Distinct(StringComparer.Ordinal).Count();
            }
            result[group.Key] = entry;
        }
        return result;
    }

    private static string Describe(string table, int index, LuxembourgIndexBuilder.ProjectedRows rows)
    {
        if (table == "work_titles")
        {
            var title = rows.WorkTitles[index];
            var sharing = rows.WorkTitles
                .Where(other => other.WorkIdentifier == title.WorkIdentifier && other.ExpressionIri == title.ExpressionIri &&
                    other.Language == title.Language && other.Title == title.Title && other.TitleKind == title.TitleKind &&
                    other.EvidenceSha256 == title.EvidenceSha256)
                .Select(static other => $"document_date={other.DocumentDate ?? "null"}")
                .ToArray();
            return $"{title.WorkIdentifier} | {title.ExpressionIri} | {title.Language} | {title.TitleKind} | " +
                $"{(title.Title.Length > 80 ? title.Title[..80] + "…" : title.Title)} | evidence {title.EvidenceSha256[..12]} | " +
                $"rows with this key: {string.Join(", ", sharing)}";
        }
        return table switch
        {
            "members" => JsonSerializer.Serialize(rows.Members[index]),
            "articles" => $"{rows.Articles[index].ArticleIdentitySha256} {rows.Articles[index].ExpressionIri} {rows.Articles[index].PublisherId}",
            "states" => $"{rows.States[index].WorkKey} {rows.States[index].ApplicabilityDate} {rows.States[index].ExpressionIri} " +
                $"{rows.States[index].Language} {rows.States[index].StateSha256} {rows.States[index].PublisherLegalResourceIri}",
            "intervals" => JsonSerializer.Serialize(rows.Intervals[index]),
            "work_facts" => JsonSerializer.Serialize(rows.WorkFacts[index]),
            _ => $"{table}[{index}]",
        };
    }

    /// <summary>The kind of an expression's legal resource: <c>jo</c>, <c>consolide</c>, <c>code</c> or <c>other</c>.</summary>
    private static string KindOf(string expressionIri) =>
        LuxembourgEliState.KindOf(expressionIri[..expressionIri.LastIndexOf('/')]) switch
        {
            "act" => "jo",
            "consolidation" => "consolide",
            "code" => "code",
            _ => "other",
        };

    private static LuxembourgRightsChannelDisposition Disposition(string wire) =>
        Enum.GetValues<LuxembourgRightsChannelDisposition>().Single(value => ContractWire.NameOf(value) == wire);

    private static JsonObject Counted(IEnumerable<string> values) =>
        new(values.GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));

    private static async Task<JsonNode> ReadJsonAsync(ICustodyStore store, string sha256) =>
        JsonNode.Parse((await CustodyRestore.ReadByDigestCheckedAsync(store, sha256, CancellationToken.None)).Span)!;

    /// <summary>A receipt naming a retained object of the run's custody by its digest and length, as the earlier probes make.</summary>
    private static async Task<DurableBlobWriteReceipt> ReceiptAsync(ICustodyStore store, string sha256)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, sha256, CancellationToken.None);
        var template = await new EuAcquisitionTestFixture.EuInMemoryCustodyStore().CreateAsync(
            "template"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var policy = template.PolicyEvidence;
        var reference = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, sha256, bytes.Length, CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            template.Schema, reference,
            new CustodyPolicyEvidence(policy.Schema, reference, policy.VerificationProfile, policy.PolicyKey,
                policy.Protection, policy.ObservedAt, policy.ProtectedUntil));
    }
}
