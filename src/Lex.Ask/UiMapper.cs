using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

/// <summary>
/// Turns one tool result into a rendering directive. This is the whole "unstructured to
/// structured" seam: the model chooses a tool and its arguments from natural language, and
/// the shape of what comes back determines what the interface draws. The model never names
/// a view and never authors a value — every field here is copied from tool output, which is
/// why a fabricated citation cannot reach the screen.
/// </summary>
/// Public so the AI-to-UI contract can be tested directly: the mapping from what the assistant
/// asked for to what the workspace does is a contract, and an untested contract is a promise.
public static class UiMapper
{
    public static UiEffect From(RequestedOperation operation, JsonNode result)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(result);
        var arguments = JsonNode.Parse(operation.Arguments.GetRawText())?.AsObject()
            ?? throw new InvalidDataException("Operation arguments must be a JSON object.");
        var effect = From(operation.Tool, arguments, result, "en");
        ValidateEffects(operation, effect);
        return effect;
    }


    public static UiEffect From(
        RequestedOperation operation,
        JsonObject executedArguments,
        JsonNode result,
        string locale = "en")
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(executedArguments);
        ArgumentNullException.ThrowIfNull(result);
        var effect = From(operation.Tool, executedArguments, result, locale);
        ValidateEffects(operation, effect);
        return effect;
    }

    public static void ValidateEffects(RequestedOperation operation, UiEffect effect)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(effect);
        var produced = EffectKinds(effect).ToArray();
        if (produced.Length == 0)
            throw new InvalidDataException(
                $"Operation '{operation.OperationId}' did not produce its typed effect.");
        var unexpected = produced.Where(item => !operation.Effects.Contains(item)).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidDataException(
                $"Operation '{operation.OperationId}' produced '{unexpected[0]}' outside its frozen plan.");
    }

    private static IEnumerable<OperationEffect> EffectKinds(UiEffect effect)
    {
        if (effect.Provision is not null) yield return OperationEffect.Provision;
        if (effect.Diff is not null) yield return OperationEffect.Diff;
        if (effect.History is not null) yield return OperationEffect.History;
        if (effect.Timeline is not null) yield return OperationEffect.Timeline;
        if (effect.Ranking is not null) yield return OperationEffect.Ranking;
        if (effect.InForce is not null) yield return OperationEffect.InForce;
        if (effect.CitedBy is not null) yield return OperationEffect.CitedBy;
        if (effect.Coverage is not null) yield return OperationEffect.Coverage;
        if (effect.Verification is not null) yield return OperationEffect.Verification;
        if (effect.Workspace is not null) yield return OperationEffect.Workspace;
        if (effect.Gap is not null) yield return OperationEffect.Gap;
    }

    public static UiEffect From(string tool, JsonObject args, JsonNode result, string locale = "en")
    {
        var evidence = EvidenceOf(result, args);
        if (tool == "coverage") return WithEvidence(CoverageResult(result, locale), evidence);
        if (result is JsonArray { Count: 0 })
        {
            var empty = tool switch
            {
                "changes_in_period" => Ranking(new JsonObject
                {
                    ["status"] = McpStatus.NoChangesInPeriod,
                    ["window"] = new JsonObject
                    {
                        ["from"] = S(args, "from_date"),
                        ["to"] = S(args, "to_date"),
                    },
                    ["order"] = S(args, "order") ?? "by_date",
                    ["works_changed"] = 0,
                    ["new_versions"] = 0,
                    ["population"] = new JsonObject
                    {
                        ["basis"] = "no mounted publisher matches the selected scope",
                        ["works_in_scope"] = 0,
                        ["known_exclusions"] = "the selected publisher or jurisdiction is not mounted",
                    },
                    ["offset"] = args["offset"]?.DeepClone(),
                    ["changes"] = new JsonArray(),
                }, args),
                "in_force_on" => InForce(new JsonObject
                {
                    ["status"] = McpStatus.NoResult,
                    ["total_works_in_force"] = 0,
                    ["works"] = new JsonArray(),
                }, args),
                "search" or "navigate" => Workspace(args),
                _ => new UiEffect(),
            };
            return WithEvidence(empty, evidence);
        }
        var node = result is JsonArray arr ? Aggregate(tool, arr) : result as JsonObject;
        if (node is null) return new UiEffect();
        var status = (node["envelope"]?["status"] ?? node["status"])?.GetValue<string>();
        var outcome = status is null ? (LegalOutcome?)null : LegalOperationPolicy.OutcomeForStatus(status);

        // A refusal is a first-class view: say what is missing and what does exist instead.
        if (status is not null && outcome is LegalOutcome.NotAvailable or LegalOutcome.NotFound
                or LegalOutcome.NotComparable)
        {
            var gap = new UiEffect(Gap: new GapView(
                Status: status,
                Work: S(node, "work") ?? S(node, "lex_id"),
                Date: S(args, "date") ?? S(args, "as_of"),
                Explanation: Explain(status, locale),
                Available: node["versions"]?.AsArray().OfType<JsonObject>()
                               .Select(v => S(v, "valid_from") ?? "").Where(s => s.Length > 0).Take(12).ToList()
                           ?? node["anchors_not_in_version"]?.AsArray().Select(a => a?.GetValue<string>() ?? "").ToList()
                           ?? []));
            var refused = outcome == LegalOutcome.NotComparable && tool == "diff"
                ? UiEffect.Merge([Diff(node, args), gap])
                : gap;
            return WithEvidence(refused, evidence);
        }

        var mapped = tool switch
        {
            "as_of" => Provision(node, args),
            "article_history" => History(node, args),
            "timeline" => Timeline(node, args),
            "diff" => Diff(node, args),
            "changes_in_period" => Ranking(node, args),
            "in_force_on" => InForce(node, args),
            "cited_by" => Cited(node),
            "provenance" => Verification(node),
            "search" => Workspace(args),
            "navigate" => Workspace(args),
            _ => new UiEffect(),
        };
        return WithEvidence(mapped, evidence);
    }

    private static UiEffect WithEvidence(
        UiEffect effect,
        IReadOnlyList<EvidenceContext> evidence) => effect with
    {
        Provision = effect.Provision is null ? null : effect.Provision with { Evidence = evidence },
        Diff = effect.Diff is null ? null : effect.Diff with { Evidence = evidence },
        History = effect.History is null ? null : effect.History with { Evidence = evidence },
        Timeline = effect.Timeline is null ? null : effect.Timeline with { Evidence = evidence },
        Ranking = effect.Ranking is null ? null : effect.Ranking with { Evidence = evidence },
        InForce = effect.InForce is null ? null : effect.InForce with { Evidence = evidence },
        CitedBy = effect.CitedBy is null ? null : effect.CitedBy with { Evidence = evidence },
        Coverage = effect.Coverage is null ? null : effect.Coverage with { Evidence = evidence },
        Verification = effect.Verification is null
            ? null : effect.Verification with { Evidence = evidence },
        Workspace = effect.Workspace is null ? null : effect.Workspace with { Evidence = evidence },
        Gap = effect.Gap is null ? null : effect.Gap with { Evidence = evidence },
    };

    private static IReadOnlyList<EvidenceContext> EvidenceOf(JsonNode result, JsonObject args)
    {
        var contexts = new List<EvidenceContext>();
        var rows = result is JsonArray array
            ? array.OfType<JsonObject>()
            : result is JsonObject item ? [item] : [];
        foreach (var row in rows.Take(8))
        {
            if (contexts.Count >= 8) break;
            var envelope = row["envelope"] as JsonObject;
            var freshness = envelope?["freshness"] as JsonObject;
            var artifact = envelope?["artifact"] as JsonObject;
            var documents = new List<JsonObject>();
            documents.AddRange(new[]
                {
                    row["document"] as JsonObject,
                    row["from"] as JsonObject,
                    row["to"] as JsonObject,
                }.Where(document => document is not null).Cast<JsonObject>());
            foreach (var field in new[] { "versions", "works", "states" })
                foreach (var document in (row[field] as JsonArray)?.OfType<JsonObject>() ?? [])
                {
                    if (documents.Count >= 8) break;
                    documents.Add(document);
                }
            if (documents.Count == 0)
                documents.Add(row);
            foreach (var document in documents)
            {
                if (contexts.Count >= 8) break;
                var versions = row["versions"] as JsonArray;
                var firstVersion = versions?.OfType<JsonObject>()
                    .OrderBy(version => S(version, "valid_from"), StringComparer.Ordinal).FirstOrDefault();
                var lastVersion = versions?.OfType<JsonObject>()
                    .OrderBy(version => S(version, "valid_from"), StringComparer.Ordinal).LastOrDefault();
                var firstProvision = (document["provisions"] as JsonArray
                    ?? row["provisions"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
                contexts.Add(new EvidenceContext(
                    Publisher: S(envelope, "publisher"),
                    Jurisdiction: S(envelope, "jurisdiction"),
                    TimelineSemantics: S(envelope, "timeline_semantics"),
                    RequestedDate: S(args, "date") ?? S(args, "as_of"),
                    RequestedFromDate: S(args, "from_date")
                        ?? S(row["window"] as JsonObject, "from"),
                    RequestedToDate: S(args, "to_date")
                        ?? S(row["window"] as JsonObject, "to"),
                    ObservedAt: S(freshness, "last_confirmed_at") ?? S(freshness, "built_at"),
                    ValidFrom: S(document, "valid_from") ?? S(firstVersion, "valid_from"),
                    ValidTo: S(document, "valid_to") ?? S(lastVersion, "valid_to"),
                    Provisional: envelope?["provisional"]?.GetValue<bool>() ?? false,
                    SourceUri: S(document, "source_uri"),
                    ExtractionProfile: S(document, "extraction_profile") ?? S(document, "profile"),
                    RecordSha256: S(document, "record_sha256"),
                    BodySha256: S(document, "body_sha256"),
                    TextSha256: S(document, "text_sha256") ?? S(firstProvision, "text_sha256"),
                    ArtifactManifestId: S(artifact, "manifest_set_id"),
                    ContentDigest: S(artifact, "content_digest"),
                    SignatureValid: freshness?["stamp_signature_valid"]?.GetValue<bool?>()));
            }
        }
        return contexts;
    }

    private static UiEffect CoverageResult(JsonNode result, string locale)
    {
        var rows = result is JsonArray array
            ? array.OfType<JsonObject>().ToList()
            : result is JsonObject item ? [item] : [];
        if (rows.Count == 0) return new UiEffect(Coverage: new CoverageView([]));
        var status = LegalOperationPolicy.StatusForResult(result);
        var outcome = LegalOperationPolicy.OutcomeForStatus(status);
        if (outcome is LegalOutcome.NotAvailable or LegalOutcome.NotFound)
            return new UiEffect(Gap: new GapView(status, null, null, Explain(status, locale), []));
        return new UiEffect(Coverage: new CoverageView(rows.Select(row =>
        {
            var envelope = row["envelope"] as JsonObject;
            var freshness = envelope?["freshness"] as JsonObject;
            return new PublisherCoverage(
                Publisher: S(envelope, "publisher") ?? "",
                Name: S(row, "publisher_name"),
                Tier: S(envelope, "tier"),
                Works: row["works"]?.GetValue<int>() ?? 0,
                Versions: row["versions"]?.GetValue<int>() ?? 0,
                VersionsWithText: row["text"]?["versions_with_text_served"]?.GetValue<int>() ?? 0,
                VersionsWithoutText: row["text"]?["versions_without_text"]?.GetValue<int>() ?? 0,
                Earliest: S(row, "valid_from_earliest"),
                Latest: S(row, "valid_from_latest"),
                InventoryStatus: S(row, "build_inventory_status"),
                BuildComplete: row["build_complete"]?.GetValue<bool?>(),
                SignatureValid: freshness?["stamp_signature_valid"]?.GetValue<bool?>(),
                KnownGaps: row["known_gaps"] is JsonArray gaps
                    ? gaps.Select(gap => gap?.GetValue<string>() ?? "")
                        .Where(gap => gap.Length > 0).Take(12).ToList()
                    : []);
        }).ToList()));
    }

    private static UiEffect Verification(JsonObject o)
    {
        var document = o["document"] as JsonObject ?? new JsonObject();
        var stamp = o["stamp"] as JsonObject ?? new JsonObject();
        return new UiEffect(Verification: new VerificationView(
            LexId: S(document, "lex_id") ?? "",
            Title: S(document, "title"),
            SourceUri: S(document, "source_uri"),
            RecordSha256: S(document, "record_sha256"),
            BodySha256: S(document, "body_sha256"),
            Permalink: S(document, "permalink"),
            SignatureValid: stamp["signature_valid"]?.GetValue<bool?>(),
            Algorithm: S(stamp, "algorithm")));
    }

    private static bool HasContent(JsonObject o)
        => o["provisions"] is JsonArray { Count: > 0 } || o["states"] is JsonArray { Count: > 0 }
           || o["changes"] is JsonArray { Count: > 0 } || o["works"] is JsonArray { Count: > 0 }
           || o["document"] is JsonObject || o["from"] is JsonObject;

    /// <summary>
    /// Corpus-wide tools return one envelope per mounted publisher. A UI effect is one view, so
    /// selecting the first non-empty envelope silently turned an EU + Luxembourg answer into
    /// whichever index happened to be enumerated first. Combine only the explicitly aggregate
    /// tool shapes and retain each row's jurisdiction before mapping the view.
    /// </summary>
    private static JsonObject? Aggregate(string tool, JsonArray result)
    {
        var parts = result.OfType<JsonObject>().ToList();
        if (parts.Count == 0) return null;
        if (tool is not ("changes_in_period" or "in_force_on" or "cited_by"))
            return parts.FirstOrDefault(HasContent) ?? parts[0];

        var combined = (parts.FirstOrDefault(HasContent) ?? parts[0]).DeepClone().AsObject();
        var field = tool switch
        {
            "changes_in_period" => "changes",
            "in_force_on" => "works",
            _ => "citations",
        };
        var rows = new JsonArray();
        foreach (var part in parts)
        {
            var jurisdiction = S(part["envelope"] as JsonObject, "jurisdiction");
            if (part[field] is not JsonArray source) continue;
            foreach (var item in source.OfType<JsonObject>())
            {
                var row = item.DeepClone().AsObject();
                if (jurisdiction is not null && row["jurisdiction"] is null)
                    row["jurisdiction"] = jurisdiction;
                rows.Add(row);
            }
        }
        combined[field] = rows;
        if (tool == "changes_in_period")
        {
            combined["works_changed"] = parts.Sum(p => p["works_changed"]?.GetValue<int>() ?? 0);
            combined["new_versions"] = parts.Sum(p => p["new_versions"]?.GetValue<int>() ?? 0);
            combined["population"] = new JsonObject
            {
                ["basis"] = "sum of the selected publisher scopes",
                ["works_in_scope"] = parts.Sum(part =>
                    part["population"]?["works_in_scope"]?.GetValue<int>() ?? 0),
                ["known_exclusions"] = new JsonArray(parts
                    .Select(part => S(part["population"] as JsonObject, "known_exclusions"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct().Select(value => (JsonNode)value!).ToArray()),
            };
        }
        else if (tool == "in_force_on")
            combined["total_works_in_force"] = parts.Sum(p => p["total_works_in_force"]?.GetValue<int>() ?? 0);
        else
            combined["citing_articles"] = rows.Count;
        return combined;
    }

    private static UiEffect Provision(JsonObject o, JsonObject args)
    {
        var doc = o["document"] as JsonObject ?? o;
        var items = (o["provisions"] as JsonArray)?.OfType<JsonObject>()
            .Select(p => new ProvisionItem(
                Anchor: S(p, "anchor") ?? "",
                Num: S(p, "num"), Heading: S(p, "heading"),
                Text: S(p, "text") ?? S(p, "text_md") ?? "",
                Sha: S(p, "text_sha256"),
                TextOmitted: p["text_omitted"]?.GetValue<bool>() == true,
                TextOmittedReason: S(p, "text_omitted_reason"),
                Permalink: S(p, "permalink"))).Where(i => i.Text.Length > 0
                    || i.Anchor.Length > 0 || !string.IsNullOrWhiteSpace(i.Heading)).ToList()
            ?? [];
        if (items.Count == 0 && S(doc, "text") is { Length: > 0 } documentText)
            items.Add(new ProvisionItem("", null, S(doc, "title"), documentText, null));
        if (items.Count == 0 && doc["text_omitted"]?.GetValue<bool>() == true)
            items.Add(new ProvisionItem("", null, S(doc, "title"), "", null,
                TextOmitted: true,
                TextOmittedReason: S(doc, "text_omitted_reason"),
                Permalink: S(doc, "permalink") ?? S(doc, "source_uri")));
        if (items.Count == 0) return new UiEffect();
        return new UiEffect(Provision: new ProvisionView(
            Subject: SubjectOf(doc, args),
            ValidFrom: S(doc, "valid_from") ?? "",
            ValidTo: S(doc, "valid_to"),
            Provisions: items,
            Permalink: S(doc, "permalink"),
            TotalProvisions: o["total_provisions"]?.GetValue<int?>(),
            Truncated: o["truncated"]?.GetValue<bool>() ?? false,
            TextTruncated: o["text_truncated"]?.GetValue<bool>() ?? false,
            OutlineOnly: S(args, "mode") == "outline"));
    }

    private static UiEffect History(JsonObject o, JsonObject args)
    {
        if (o["states"] is not JsonArray states || states.Count == 0) return new UiEffect();
        return new UiEffect(History: new HistoryView(
            Subject: new Subject(CanonicalWork(o, args), null, null, S(o, "anchor"),
                S(o, "language") ?? S(args, "language")),
            Anchor: S(o, "anchor") ?? "",
            DistinctTexts: o["distinct_texts"]?.GetValue<int>() ?? states.Count,
            States: states.OfType<JsonObject>().Select(s => new HistoryState(
                S(s, "valid_from") ?? "", S(s, "valid_to"), S(s, "text_sha256"), S(s, "permalink"))).ToList()));
    }

    private static UiEffect Timeline(JsonObject o, JsonObject args)
    {
        if (o["versions"] is not JsonArray versions || versions.Count == 0) return new UiEffect();
        var rows = versions.OfType<JsonObject>()
            .OrderBy(version => S(version, "valid_from"), StringComparer.Ordinal)
            .ToList();
        var latest = rows[^1];
        return new UiEffect(Timeline: new TimelineView(
            Subject: new Subject(CanonicalWork(o, args), S(latest, "title"), null, null,
                S(latest, "language") ?? S(args, "language"))));
    }

    private static UiEffect Diff(JsonObject o, JsonObject args)
    {
        var from = S(args, "from_date") ?? S(o, "from_date");
        var to = S(args, "to_date") ?? S(o, "to_date");
        if (from is null || to is null) return new UiEffect();
        // diff returns the two resolved documents as `from` / `to`, not a list.
        var a = o["from"] as JsonObject;
        var b = o["to"] as JsonObject;
        return new UiEffect(Diff: new DiffView(
            Subject: new Subject(CanonicalWork(o, args),
                S(b, "title") ?? S(a, "title"), from, S(o, "anchor"),
                S(b, "language") ?? S(a, "language") ?? S(args, "language")),
            FromDate: from, ToDate: to,
            FromPermalink: S(a, "permalink"), ToPermalink: S(b, "permalink"),
            Note: S(o, "note"),
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status")));
    }

    /// Controls the assistant set on the way to its answer, so the workspace lands the same way.
    private static UiEffect Workspace(JsonObject args, int? page = null)
    {
        var view = new WorkspaceView(
            Query: S(args, "query"),
            Jurisdiction: S(args, "jurisdiction"),
            Hierarchy: S(args, "hierarchy"),
            Domain: S(args, "domain"),
            SourceClass: S(args, "source_class") ?? S(args, "document_type"),
            ActForm: S(args, "act_form"),
            BindingStatus: S(args, "binding_status"),
            Page: page,
            Language: S(args, "language"),
            Work: S(args, "work"),
            Date: S(args, "date"),
            Anchor: S(args, "anchor"));
        return view is { Query: null, Jurisdiction: null, Hierarchy: null, Domain: null, SourceClass: null,
                         ActForm: null, BindingStatus: null, Page: null, Language: null,
                         Work: null, Date: null, Anchor: null }
            ? new UiEffect()
            : new UiEffect(Workspace: view);
    }

    private static UiEffect Cited(JsonObject o)
    {
        if (o["citations"] is not JsonArray rows) return new UiEffect();
        return new UiEffect(CitedBy: new CitedByView(
            CitedWork: S(o, "cited_work") ?? "",
            CitingArticles: o["citing_articles"]?.GetValue<int>() ?? rows.Count,
            Rows: rows.OfType<JsonObject>().Select(c => new CitedByRow(
                Work: S(c, "work") ?? "", Title: S(c, "title"), ValidFrom: S(c, "valid_from") ?? "",
                Anchor: S(c, "anchor") ?? "", Num: S(c, "num"), Permalink: S(c, "permalink"),
                Jurisdiction: S(c, "jurisdiction"))).ToList(),
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status")));
    }

    private static UiEffect Ranking(JsonObject o, JsonObject args)
    {
        if (o["changes"] is not JsonArray rows) return new UiEffect();
        var offset = o["offset"]?.GetValue<int>() ?? 0;
        var jurisdiction = S(o["envelope"] as JsonObject, "jurisdiction");
        return new UiEffect(Ranking: new RankingView(
            FromDate: S(o["window"] as JsonObject ?? [], "from") ?? "",
            ToDate: S(o["window"] as JsonObject ?? [], "to") ?? "",
            Order: S(o, "order") ?? "by_date",
            WorksChanged: o["works_changed"]?.GetValue<int>() ?? rows.Count,
            NewVersions: o["new_versions"]?.GetValue<int>() ?? 0,
            Rows: rows.OfType<JsonObject>().Select(c => new RankingRow(
                Work: S(c, "work") ?? "", Title: S(c, "title"),
                VersionsInPeriod: c["versions_in_period"]?.GetValue<int>() ?? 0,
                VersionsTotal: c["versions_total"]?.GetValue<int>() ?? 0,
                FirstChange: S(c, "first_change") ?? "", LastChange: S(c, "last_change") ?? "",
                Baseline: S(c, "baseline"), DiffFrom: S(c, "diff_from"), DiffTo: S(c, "diff_to"),
                DistinctTexts: c["distinct_texts"]?.GetValue<int>() ?? 0,
                WordingChanged: c["wording_changed"]?.GetValue<bool>() ?? true,
                TextComparable: c["text_comparable"]?.GetValue<bool>() ?? false,
                Jurisdiction: S(c, "jurisdiction") ?? jurisdiction, Hierarchy: S(c, "hierarchy"),
                Domains: c["domains"] is JsonArray domains
                    ? domains.Select(d => d?.GetValue<string>() ?? "").Where(d => d.Length > 0).ToList()
                    : null,
                SourceClass: S(c, "source_class"), ActForm: S(c, "act_form"),
                BindingStatus: S(c, "binding_status"), Language: S(c, "language"),
                Permalink: S(c, "permalink"), DiffPermalink: S(c, "diff_permalink"))).ToList(),
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status"),
            PopulationWorks: o["population"]?["works_in_scope"]?.GetValue<int>() ?? 0,
            PopulationBasis: S(o["population"] as JsonObject, "basis"),
            KnownExclusions: o["population"]?["known_exclusions"] is JsonArray exclusions
                ? exclusions.Select(value => value?.GetValue<string>() ?? "")
                    .Where(value => value.Length > 0).Take(8).ToArray()
                : S(o["population"] as JsonObject, "known_exclusions") is { } exclusion
                    ? [exclusion] : []),
            Workspace: Workspace(args, offset > 0 ? offset / 25 : null).Workspace);
    }

    private static UiEffect InForce(JsonObject o, JsonObject args)
    {
        if (o["works"] is not JsonArray docs) return new UiEffect();
        return new UiEffect(InForce: new InForceView(
            Date: S(args, "date") ?? "",
            Total: o["total_works_in_force"]?.GetValue<int>() ?? docs.Count,
            Rows: docs.OfType<JsonObject>().Take(60).Select(d => new InForceRow(
                Work: WorkOf(S(d, "lex_id")) ?? S(d, "work") ?? "", Title: S(d, "title"), Kind: S(d, "document_type"),
                ValidFrom: S(d, "valid_from") ?? "", Permalink: S(d, "permalink"),
                Jurisdiction: S(d, "jurisdiction"), Hierarchy: S(d, "hierarchy"))).ToList(),
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status")),
            Workspace: Workspace(args).Workspace);
    }

    private static Subject SubjectOf(JsonObject doc, JsonObject args) => new(
        Work: WorkOf(S(doc, "lex_id")) ?? CanonicalWork(doc, args),
        Title: S(doc, "title"),
        Date: S(args, "date") ?? S(doc, "valid_from"),
        Anchor: S(args, "anchors")?.Split(',')[0].Trim(),
        Language: S(doc, "language") ?? S(args, "language"));

    private static string CanonicalWork(JsonObject result, JsonObject args)
    {
        static string? Canonical(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var parts = value.Split(':');
            return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : value;
        }

        foreach (var lexId in new[]
                 {
                     S(result["document"] as JsonObject, "lex_id"),
                     S(result["from"] as JsonObject, "lex_id"),
                     S(result["to"] as JsonObject, "lex_id"),
                     S(result["versions"]?[0] as JsonObject, "lex_id"),
                     S(result["states"]?[0] as JsonObject, "in_version"),
                 })
            if (WorkOf(lexId) is { } work) return work;
        var returned = Canonical(S(result, "work"));
        if (returned?.Contains(':') == true) return returned;
        var publisher = S(result["envelope"] as JsonObject, "publisher");
        if (publisher is not null && returned is not null) return $"{publisher}:{returned}";
        return Canonical(S(args, "work")) ?? returned ?? "";
    }

    private static string? WorkOf(string? lexId)
    {
        if (lexId is null) return null;
        var p = lexId.Split(':');
        return p.Length >= 2 ? $"{p[0]}:{p[1]}" : lexId;
    }

    private static string Explain(string status, string locale)
    {
        if (locale == "fr")
            return status switch
            {
                McpStatus.NoCorpusMounted => "Lex ne dispose d'aucun index juridique vérifié, donc aucune opération juridique n'est disponible.",
                McpStatus.NoVersionForDate => "Lex détient cet instrument, mais aucune version de l'éditeur ne couvre cette date.",
                McpStatus.UnknownWork => "Lex ne détient pas cet instrument.",
                McpStatus.UnknownAnchor => "Cet identifiant d'article n'existe pas dans cet instrument.",
                McpStatus.AnchorNotInVersion => "Cet article n'existait pas dans la version de l'éditeur sélectionnée pour cette date.",
                McpStatus.TextWithheld => "Lex détient cette version et son texte, mais une règle de publication empêche d'en servir le libellé.",
                McpStatus.TextNotAvailable => "Lex détient la notice et les dates de l'éditeur, mais aucun texte de disposition ne peut être servi de manière fiable.",
                McpStatus.NoProvisionHistory => "Lex détient cet instrument sans historique par article, donc un article isolé ne peut pas être suivi dans le temps.",
                McpStatus.ProfilesDiffer => "Lex détient les deux versions, mais leurs profils d'extraction ne permettent pas une comparaison fiable.",
                _ => "Lex ne peut pas répondre à partir de ce qu'il détient.",
            };
        return status switch
        {
            McpStatus.NoCorpusMounted => "Lex has no verified legal index mounted, so no legal operation is available.",
            McpStatus.NoVersionForDate => "Lex holds this law, but no publisher version covers that date.",
            McpStatus.UnknownWork => "Lex does not hold this work at all.",
            McpStatus.UnknownAnchor => "That article identifier does not exist in this law.",
            McpStatus.AnchorNotInVersion => "That article did not exist in the publisher version selected for that date.",
            McpStatus.TextWithheld => "Lex holds this version and its text, but a publication gate prevents serving the wording.",
            McpStatus.TextNotAvailable => "Lex holds this publisher record and dates, but no safely derived provision text is available.",
            McpStatus.NoProvisionHistory => "Lex holds this work without per-article history, so single articles cannot be traced through time.",
            McpStatus.ProfilesDiffer => "Lex holds both versions, but their extraction profiles do not support a reliable provision comparison.",
            _ => "Lex cannot answer this from what it holds.",
        };
    }

    // Nullable on purpose: callers reach into optional sub-objects (`o["from"] as JsonObject`),
    // and a tool response that omits one of them must map to a missing field, not to a throw that
    // loses the whole answer along with its UI payload.
    private static string? S(JsonObject? o, string k)
        => o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
