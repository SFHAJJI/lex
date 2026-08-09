using System.Text.Json.Nodes;

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
    public static UiEffect From(string tool, JsonObject args, JsonNode result)
    {
        var node = result is JsonArray arr ? Aggregate(tool, arr) : result as JsonObject;
        if (node is null) return new UiEffect();
        var status = (node["envelope"]?["status"] ?? node["status"])?.GetValue<string>();

        // A refusal is a first-class view: say what is missing and what does exist instead.
        if (status is "no_version_for_date" or "unknown_work" or "unknown_anchor"
            or "anchor_not_in_version" or "text_withheld" or "text_not_available" or "no_provision_history"
            or "outside_observed_window")
            return new UiEffect(Gap: new GapView(
                Status: status,
                Work: S(node, "work") ?? S(node, "lex_id"),
                Date: S(args, "date") ?? S(args, "as_of"),
                Explanation: Explain(status),
                Available: node["versions"]?.AsArray().OfType<JsonObject>()
                               .Select(v => S(v, "valid_from") ?? "").Where(s => s.Length > 0).Take(12).ToList()
                           ?? node["anchors_not_in_version"]?.AsArray().Select(a => a?.GetValue<string>() ?? "").ToList()
                           ?? []));

        return tool switch
        {
            "as_of" => Provision(node, args),
            "article_history" => History(node),
            "diff" => Diff(node, args),
            "changes_in_period" => Ranking(node, args),
            "in_force_on" => InForce(node, args),
            "cited_by" => Cited(node),
            "search" => Workspace(args),
            _ => new UiEffect(),
        };
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
        if (o["provisions"] is not JsonArray provs || provs.Count == 0) return new UiEffect();
        var items = provs.OfType<JsonObject>().Select(p => new ProvisionItem(
            Anchor: S(p, "anchor") ?? "",
            Num: S(p, "num"), Heading: S(p, "heading"),
            Text: S(p, "text") ?? S(p, "text_md") ?? "",
            Sha: S(p, "text_sha256"))).Where(i => i.Text.Length > 0
                || i.Anchor.Length > 0 || !string.IsNullOrWhiteSpace(i.Heading)).ToList();
        if (items.Count == 0) return new UiEffect();
        return new UiEffect(Provision: new ProvisionView(
            Subject: SubjectOf(doc, args),
            ValidFrom: S(doc, "valid_from") ?? "",
            ValidTo: S(doc, "valid_to"),
            Provisions: items,
            Permalink: S(doc, "permalink")));
    }

    private static UiEffect History(JsonObject o)
    {
        if (o["states"] is not JsonArray states || states.Count == 0) return new UiEffect();
        return new UiEffect(History: new HistoryView(
            Subject: new Subject(S(o, "work") ?? "", null, null, S(o, "anchor")),
            Anchor: S(o, "anchor") ?? "",
            DistinctTexts: o["distinct_texts"]?.GetValue<int>() ?? states.Count,
            States: states.OfType<JsonObject>().Select(s => new HistoryState(
                S(s, "valid_from") ?? "", S(s, "valid_to"), S(s, "text_sha256"), S(s, "permalink"))).ToList()));
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
            Subject: new Subject(S(args, "work") ?? S(o, "work") ?? "", S(b, "title") ?? S(a, "title"), from, null),
            FromDate: S(a, "valid_from") ?? from, ToDate: S(b, "valid_from") ?? to,
            FromPermalink: S(a, "permalink"), ToPermalink: S(b, "permalink"),
            Note: S(o, "note")));
    }

    /// Controls the assistant set on the way to its answer, so the workspace lands the same way.
    private static UiEffect Workspace(JsonObject args, int? page = null)
    {
        var view = new WorkspaceView(
            Jurisdiction: S(args, "jurisdiction"),
            Hierarchy: S(args, "hierarchy"),
            Domain: S(args, "domain"),
            SourceClass: S(args, "source_class") ?? S(args, "document_type"),
            ActForm: S(args, "act_form"),
            BindingStatus: S(args, "binding_status"),
            Page: page,
            Language: S(args, "language"));
        return view is { Jurisdiction: null, Hierarchy: null, Domain: null, SourceClass: null,
                         ActForm: null, BindingStatus: null, Page: null, Language: null }
            ? new UiEffect()
            : new UiEffect(Workspace: view);
    }

    private static UiEffect Cited(JsonObject o)
    {
        if (o["citations"] is not JsonArray rows || rows.Count == 0) return new UiEffect();
        return new UiEffect(CitedBy: new CitedByView(
            CitedWork: S(o, "cited_work") ?? "",
            CitingArticles: o["citing_articles"]?.GetValue<int>() ?? rows.Count,
            Rows: rows.OfType<JsonObject>().Select(c => new CitedByRow(
                Work: S(c, "work") ?? "", Title: S(c, "title"), ValidFrom: S(c, "valid_from") ?? "",
                Anchor: S(c, "anchor") ?? "", Num: S(c, "num"), Permalink: S(c, "permalink"),
                Jurisdiction: S(c, "jurisdiction"))).ToList()));
    }

    private static UiEffect Ranking(JsonObject o, JsonObject args)
    {
        if (o["changes"] is not JsonArray rows || rows.Count == 0) return new UiEffect();
        var offset = o["offset"]?.GetValue<int>() ?? 0;
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
                Jurisdiction: S(c, "jurisdiction"), Hierarchy: S(c, "hierarchy"),
                Domains: c["domains"] is JsonArray domains
                    ? domains.Select(d => d?.GetValue<string>() ?? "").Where(d => d.Length > 0).ToList()
                    : null,
                SourceClass: S(c, "source_class"), ActForm: S(c, "act_form"),
                BindingStatus: S(c, "binding_status"), Language: S(c, "language"),
                Permalink: S(c, "permalink"), DiffPermalink: S(c, "diff_permalink"))).ToList()),
            Workspace: Workspace(args, offset > 0 ? offset / 25 : null).Workspace);
    }

    private static UiEffect InForce(JsonObject o, JsonObject args)
    {
        if (o["works"] is not JsonArray docs || docs.Count == 0) return new UiEffect();
        return new UiEffect(InForce: new InForceView(
            Date: S(args, "date") ?? "",
            Total: o["total_works_in_force"]?.GetValue<int>() ?? docs.Count,
            Rows: docs.OfType<JsonObject>().Take(60).Select(d => new InForceRow(
                Work: WorkOf(S(d, "lex_id")) ?? S(d, "work") ?? "", Title: S(d, "title"), Kind: S(d, "document_type"),
                ValidFrom: S(d, "valid_from") ?? "", Permalink: S(d, "permalink"),
                Jurisdiction: S(d, "jurisdiction"), Hierarchy: S(d, "hierarchy"))).ToList()),
            Workspace: Workspace(args).Workspace);
    }

    private static Subject SubjectOf(JsonObject doc, JsonObject args) => new(
        Work: S(args, "work") ?? WorkOf(S(doc, "lex_id")) ?? "",
        Title: S(doc, "title"),
        Date: S(args, "date") ?? S(doc, "valid_from"),
        Anchor: S(args, "anchors")?.Split(',')[0].Trim());

    private static string? WorkOf(string? lexId)
    {
        if (lexId is null) return null;
        var p = lexId.Split(':');
        return p.Length >= 2 ? $"{p[0]}:{p[1]}" : lexId;
    }

    private static string Explain(string status) => status switch
    {
        "no_version_for_date" => "Lex holds this law, but no publisher version covers that date.",
        "unknown_work" => "Lex does not hold this work at all.",
        "unknown_anchor" => "That article identifier does not exist in this law.",
        "anchor_not_in_version" => "That article did not exist in the publisher version selected for that date.",
        "text_withheld" => "Lex holds this version and its text, but a publication gate prevents serving the wording.",
        "text_not_available" => "Lex holds this publisher record and dates, but no safely derived provision text is available.",
        "no_provision_history" => "Lex holds this work without per-article history, so single articles cannot be traced through time.",
        "outside_observed_window" => "That date falls outside the window Lex has observed.",
        _ => "Lex cannot answer this from what it holds.",
    };

    // Nullable on purpose: callers reach into optional sub-objects (`o["from"] as JsonObject`),
    // and a tool response that omits one of them must map to a missing field, not to a throw that
    // loses the whole answer along with its UI payload.
    private static string? S(JsonObject? o, string k)
        => o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
