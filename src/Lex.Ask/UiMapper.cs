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
        var node = result is JsonArray arr ? arr.OfType<JsonObject>().FirstOrDefault(HasContent) ?? arr.OfType<JsonObject>().FirstOrDefault() : result as JsonObject;
        if (node is null) return new UiEffect();
        var status = (node["envelope"]?["status"] ?? node["status"])?.GetValue<string>();

        // A refusal is a first-class view: say what is missing and what does exist instead.
        if (status is "no_version_for_date" or "unknown_work" or "unknown_anchor"
            or "anchor_not_in_version" or "text_withheld" or "no_provision_history"
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

    private static UiEffect Provision(JsonObject o, JsonObject args)
    {
        var doc = o["document"] as JsonObject ?? o;
        if (o["provisions"] is not JsonArray provs || provs.Count == 0) return new UiEffect();
        var items = provs.OfType<JsonObject>().Select(p => new ProvisionItem(
            Anchor: S(p, "anchor") ?? "",
            Num: S(p, "num"), Heading: S(p, "heading"),
            Text: S(p, "text") ?? S(p, "text_md") ?? "",
            Sha: S(p, "text_sha256"))).Where(i => i.Text.Length > 0).ToList();
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

    /// <summary>
    /// Which of the workspace's layers a set of document types corresponds to. The assistant
    /// asks the index in the index's vocabulary ("LOI,CODE"); the workspace thinks in layers. This
    /// is the translation, so that asking the assistant for statutes leaves the reader looking at
    /// the Statutes tab rather than at an unexplained subset of everything.
    /// </summary>
    private static string? LayerOf(string? types) => types?.Replace(" ", "") switch
    {
        null or "" => null,
        "!RECUEIL,!CODE_RECUEIL" => "instruments",
        "Constitution,CONV,PROT,TC,ORD" => "constitution",
        "LOI,CODE" => "statutes",
        "RGD,RMIN,AMIN,AGD,RGC,AGC,ARGD,RI" => "regulations",
        "RECUEIL,CODE_RECUEIL" => "collections",
        var t when t.Contains("Constitution") => "constitution",
        var t when t.Contains("LOI") || t.Contains("CODE") => "statutes",
        var t when t.Contains("RGD") || t.Contains("RMIN") => "regulations",
        _ => null,
    };

    /// Controls the assistant set on the way to its answer, so the workspace lands the same way.
    private static UiEffect Workspace(JsonObject args)
    {
        var layer = LayerOf(S(args, "document_type"));
        var lang = S(args, "language");
        return layer is null && lang is null
            ? new UiEffect()
            : new UiEffect(Workspace: new WorkspaceView(Layer: layer, Language: lang));
    }

    private static UiEffect Cited(JsonObject o)
    {
        if (o["citations"] is not JsonArray rows || rows.Count == 0) return new UiEffect();
        return new UiEffect(CitedBy: new CitedByView(
            CitedWork: S(o, "cited_work") ?? "",
            CitingArticles: o["citing_articles"]?.GetValue<int>() ?? rows.Count,
            Rows: rows.OfType<JsonObject>().Select(c => new CitedByRow(
                Work: S(c, "work") ?? "", Title: S(c, "title"), ValidFrom: S(c, "valid_from") ?? "",
                Anchor: S(c, "anchor") ?? "", Num: S(c, "num"), Permalink: S(c, "permalink"))).ToList()));
    }

    private static UiEffect Ranking(JsonObject o, JsonObject args)
    {
        if (o["changes"] is not JsonArray rows || rows.Count == 0) return new UiEffect();
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
                Permalink: S(c, "permalink"), DiffPermalink: S(c, "diff_permalink"))).ToList()),
            Workspace: LayerOf(S(args, "document_type")) is { } lay
                ? new WorkspaceView(Layer: lay, Page: (o["offset"]?.GetValue<int>() ?? 0) / 25)
                : null);
    }

    private static UiEffect InForce(JsonObject o, JsonObject args)
    {
        if (o["works"] is not JsonArray docs || docs.Count == 0) return new UiEffect();
        return new UiEffect(InForce: new InForceView(
            Date: S(args, "date") ?? "",
            Total: o["total_works_in_force"]?.GetValue<int>() ?? docs.Count,
            Rows: docs.OfType<JsonObject>().Take(60).Select(d => new InForceRow(
                Work: S(d, "lex_id") ?? "", Title: S(d, "title"), Kind: S(d, "document_type"),
                ValidFrom: S(d, "valid_from") ?? "", Permalink: S(d, "permalink"))).ToList()));
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
        "no_version_for_date" => "Lex holds this law, but no version of it was in force on that date.",
        "unknown_work" => "Lex does not hold this work at all.",
        "unknown_anchor" => "That article identifier does not exist in this law.",
        "anchor_not_in_version" => "That article did not exist in the version in force on that date.",
        "text_withheld" => "Lex holds this version's record and dates, but the publisher offers no machine-readable text for it.",
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
