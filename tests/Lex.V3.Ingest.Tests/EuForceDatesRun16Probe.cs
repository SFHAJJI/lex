using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The plan's item 2.1 measured: the EU index /6's force dates over EU run 16's custody, without a derive. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like S6's run-15 probe. It runs only with <c>LEX_EU_FORCE_DATES_PROBE=1</c>;
/// <c>LEX_EU_FORCE_DATES_PROBE_CUSTODY</c> names run 16's custody root (its brotli store, read in place and never written:
/// <see cref="HeldOnlyCustody"/> answers the product's idempotent holds only for bytes it already holds at that address);
/// <c>LEX_EU_FORCE_DATES_PROBE_STATES</c> names derive-c's EU index, opened read-only, whose states table is run 16's census
/// (the works the build attributes dates to); <c>LEX_EU_FORCE_DATES_PROBE_OUT</c> names the summary file written. Nothing
/// goes over the network.
/// </para>
/// <para>
/// WHAT IT RUNS. The run's acquisition catalog names its query checkpoint; every family-A batch it lists is restored through
/// the product's checkpoint door (<see cref="EuEnumerationCheckpoint.RestoreReceiptAsync"/>), its rows verified as the
/// derive verifies them (<see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>), decoded by the accepted E1 contract's door
/// (<see cref="EuReifiedAxiomDecode.TryDecode"/>), projected by the build's own projection
/// (<see cref="EuropeIndexBuilder.ProjectForceDates"/>) and reopened by the reader's own check
/// (<see cref="EuropeIndexBuilder.ValidateForceDateEvidence"/>). The derive narrows each batch to each seed's closure before
/// decoding; that narrowing only repeats a statement of a work two closures share, which the projection makes one row.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuForceDatesRun16Probe
{
    private const string EnableVariable = "LEX_EU_FORCE_DATES_PROBE";
    private const string CustodyVariable = "LEX_EU_FORCE_DATES_PROBE_CUSTODY";
    private const string StatesVariable = "LEX_EU_FORCE_DATES_PROBE_STATES";
    private const string OutVariable = "LEX_EU_FORCE_DATES_PROBE_OUT";

    [TestMethod]
    public async Task TheForceDatesOverRun16AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_EU_FORCE_DATES_PROBE=1, LEX_EU_FORCE_DATES_PROBE_CUSTODY, LEX_EU_FORCE_DATES_PROBE_STATES and LEX_EU_FORCE_DATES_PROBE_OUT for the read-only run-16 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(CustodyVariable);
        var statesPath = Environment.GetEnvironmentVariable(StatesVariable);
        var output = Environment.GetEnvironmentVariable(OutVariable);
        Assert.IsTrue(Directory.Exists(root), CustodyVariable + " names run 16's custody.");
        Assert.IsTrue(File.Exists(statesPath), StatesVariable + " names derive-c's EU index.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(output), OutVariable + " names the summary.");
        var clock = Stopwatch.StartNew();

        // ---- The census: run 16's works, as derive-c's EU index holds them. ----
        var states = new List<EuropeIndexState>();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = statesPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT state_identity_sha256,seed_celex,root_work_iri,publisher_work_iri,publisher_work_celex,date_status FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                states.Add(new EuropeIndexState(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), (EuropeIndexStateDateStatus)reader.GetInt32(5), null, "{}"));
        }

        SqliteConnection.ClearAllPools();

        // ---- Family A: every delivered batch, restored, verified and decoded through the product's doors. ----
        var store = new HeldOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(root!));
        var pointer = Directory.GetFiles(root!, "eu-acquisition-*.json").Single();
        var catalogDigest = JsonNode.Parse(File.ReadAllText(pointer))!["sha256"]!.GetValue<string>();
        var catalog = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(catalogDigest, CancellationToken.None)).Span))!;
        var queryDigest = catalog["query"]!["sha256"]!.GetValue<string>();
        var query = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(queryDigest, CancellationToken.None)).Span))!;
        var profile = EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ReifiedAxiomFacts);
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        var bindings = new List<EuDateAxiomBinding>();
        var batches = 0;
        var rowsRead = 0;
        foreach (var family in query["objects"]!.AsArray())
        {
            if (family!["set"]!.GetValue<string>() != nameof(EuObjectFactsQuerySet.ReifiedAxiomFacts)) continue;
            SourceArtifactRef Ref(string name) => new(family[name]!["resource_id"]!.GetValue<string>(), family[name]!["sha256"]!.GetValue<string>());
            var receipt = await EuEnumerationCheckpoint.RestoreReceiptAsync(store, Ref("checkpoint"), Ref("run"), Ref("profile"), CancellationToken.None);
            RepeatedEnumerationInterpretationProfileIdentity.Validate(Ref("profile"), profile);
            var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal)
                ?? throw new InvalidDataException("A family-A batch does not prove its enumeration: " + proofRefusal);
            var pages = new List<RepeatedEnumerationResolvedEvidence>();
            foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static page => page.Ordinal))
                pages.Add(await glue.ReopenPageEvidenceAsync(page.Evidence, CancellationToken.None));
            var rows = VerifiedRepeatedEnumerationRows.TryOpen(proof, receipt.Delivery, profile, receipt.Delivery.InterpretationProfileRef,
                receipt.Delivery.CountA.HttpEvidenceRef, pages, out var rowsRefusal) ?? throw new InvalidDataException("A family-A batch's rows: " + rowsRefusal);
            rowsRead += rows.Count;
            bindings.AddRange(EuReifiedAxiomDecode.TryDecode(rows, profile, proof.InterpretationProfileRef.ResourceId, out var refusal, out var offending)
                ?? throw new InvalidDataException($"A family-A batch's decode: {refusal} at {offending}"));
            batches++;
        }

        // ---- The build's projection and the reader's check, over them. ----
        var forceDates = EuropeIndexBuilder.ProjectForceDates(bindings, states);
        EuropeIndexBuilder.ValidateForceDateEvidence(forceDates, states);

        var seeds = states.Where(static state => state.DateStatus == EuropeIndexStateDateStatus.OriginalWording)
            .ToDictionary(static state => state.PublisherWorkIri, static state => state.SeedCelex, StringComparer.Ordinal);
        var byAct = forceDates.Where(row => seeds.ContainsKey(row.PublisherWorkIri))
            .GroupBy(row => seeds[row.PublisherWorkIri], StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        static bool Directive(string seed) => seed.Length > 5 && seed[5] == 'L';
        static bool Regulation(string seed) => seed.Length > 5 && seed[5] == 'R';
        static string Codes(string? note) => note is null ? "-" : Regex.Replace(note, @"\{([^{}|]+)\|[^{}]*\}", "$1");
        JsonObject ActsPerRole(Func<string, bool> kind) => new(EuropeForceDateRoles.InOrder.Select(role => KeyValuePair.Create(role,
            (JsonNode?)seeds.Values.Where(kind).Count(seed => byAct.TryGetValue(seed, out var rows) && rows.Any(row => row.Role == role)))));
        string[] Several(string role) => [.. byAct
            .Where(pair => pair.Value.Where(row => row.Role == role).Select(static row => row.DateValue).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(pair => $"{pair.Key} {string.Join(",", pair.Value.Where(row => row.Role == role).Select(static row => row.DateValue).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)];

        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-eu-force-dates-run16-probe/1",
            ["catalog_sha256"] = catalogDigest,
            ["query_checkpoint_sha256"] = queryDigest,
            ["census_works"] = states.Count,
            ["acts"] = seeds.Count,
            ["acts_directives"] = seeds.Values.Count(Directive),
            ["acts_regulations"] = seeds.Values.Count(Regulation),
            ["family_a_batches"] = batches,
            ["family_a_rows_verified"] = rowsRead,
            ["statements_decoded"] = bindings.Count,
            ["force_date_rows"] = forceDates.Length,
            ["rows_by_role"] = Counted(forceDates.Select(static row => row.Role)),
            ["rows_by_contract_role"] = Counted(forceDates.Select(static row => row.FactRole)),
            ["rows_by_property_and_code"] = Counted(forceDates.Select(static row => $"{row.SourcePredicateIri[(row.SourcePredicateIri.IndexOf('#') + 1)..]} {row.QualifierCode ?? "-"}")),
            ["rows_on_works_not_an_act"] = forceDates.Count(row => !seeds.ContainsKey(row.PublisherWorkIri)),
            ["acts_with_a_date"] = byAct.Count,
            ["acts_with_each_role"] = ActsPerRole(static _ => true),
            ["directives_with_each_role"] = ActsPerRole(Directive),
            ["regulations_with_each_role"] = ActsPerRole(Regulation),
            ["acts_with_none_of_the_named_roles"] = Strings(seeds.Values.Where(seed => !byAct.TryGetValue(seed, out var rows) ||
                !rows.Any(row => EuropeForceDateRoles.Named.Contains(row.Role))).Order(StringComparer.Ordinal)),
            ["acts_without_entry_into_force"] = Strings(seeds.Values.Where(seed => !byAct.TryGetValue(seed, out var rows) ||
                rows.All(row => row.Role != EuropeForceDateRoles.EntryIntoForce)).Order(StringComparer.Ordinal)),
            ["conflicts_two_entry_into_force_dates"] = Strings(Several(EuropeForceDateRoles.EntryIntoForce)),
            ["two_statements_of_one_role_and_date"] = Strings(byAct.SelectMany(pair => pair.Value.GroupBy(static row => (row.Role, row.DateValue))
                .Where(static group => group.Count() > 1).Select(group => $"{pair.Key} {group.Key.Role} {group.Key.DateValue} x{group.Count()}")).Order(StringComparer.Ordinal)),
            ["acts_with_several_application_dates"] = Several(EuropeForceDateRoles.Application).Length,
            ["acts_with_several_end_of_validity_dates"] = Strings(Several(EuropeForceDateRoles.EndOfValidity)),
            ["end_of_validity_open_ended"] = forceDates.Count(static row => row.Role == EuropeForceDateRoles.EndOfValidity && row.OpenEnded),
            ["end_of_validity_first_note_code"] = Counted(forceDates.Where(static row => row.Role == EuropeForceDateRoles.EndOfValidity)
                .Select(static row => Codes(row.PublisherComment).Split(' ')[0])),
            ["not_pinned"] = Strings(forceDates.Where(static row => row.Role == EuropeForceDateRoles.NotPinned)
                .Select(row => $"{(seeds.TryGetValue(row.PublisherWorkIri, out var seed) ? seed : row.PublisherWorkIri)} {row.DateValue} {row.QualifierCode ?? "-"} | {Codes(row.PublisherComment)}").Order(StringComparer.Ordinal)),
            ["deadlines_with_a_type_of_date_code"] = forceDates.Count(static row => row.Role == EuropeForceDateRoles.Deadline && row.QualifierCode is not null),
            ["deadlines_without_a_note"] = forceDates.Count(static row => row.Role == EuropeForceDateRoles.Deadline && row.PublisherComment is null),
            ["deadline_note_codes"] = Counted(forceDates.Where(static row => row.Role == EuropeForceDateRoles.Deadline && row.PublisherComment is not null)
                .SelectMany(static row => Regex.Matches(row.PublisherComment!, @"\{([^{}|]+)\|").Select(static match => match.Groups[1].Value).Distinct(StringComparer.Ordinal))),
            ["deadlines_on_directives"] = forceDates.Count(row => row.Role == EuropeForceDateRoles.Deadline && seeds.TryGetValue(row.PublisherWorkIri, out var seed) && Directive(seed)),
            ["datatypes"] = Counted(forceDates.Select(static row => row.DatatypeIri)),
            ["statements_with_an_unmodelled_property"] = forceDates.Count(static row => JsonSerializer.Deserialize<string[][]>(row.QualifiersJson)!
                .Any(static pair => !pair[0].StartsWith("http://www.w3.org/", StringComparison.Ordinal) &&
                    !pair[0].EndsWith("#type_of_date", StringComparison.Ordinal) && !pair[0].EndsWith("#comment_on_date", StringComparison.Ordinal))),
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var text = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(output!, text);
        Console.WriteLine(text);
        Assert.IsGreaterThan(0, forceDates.Length);
    }

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(static value => (JsonNode?)value)]);

    private static JsonObject Counted(IEnumerable<string> values) =>
        new(values.GroupBy(static value => value, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));

    /// <summary>
    /// The run's custody, read and never written. A hold the product's restore asks for is answered only for bytes the store
    /// already holds at that address, read back and checked; nothing is created, renamed or deleted.
    /// </summary>
    internal sealed class HeldOnlyCustody(ICustodyStore inner) : ICustodyStore
    {
        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var reference = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, CustodyDigest.Of(bytes.Span), bytes.Length, custodyClass);
            _ = await inner.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
            return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference,
                new CustodyPolicyEvidence(CustodySchemaIds.CustodyPolicyEvidence, reference, CustodyVerificationProfile.FileSystemUnenforced1,
                    null, CustodyProtection.NotEnforced, DateTimeOffset.UtcNow, null));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }
}
