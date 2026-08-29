using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    internal const int MaximumSearchFactsJsonCharacters = 1_800;
    private static readonly JsonSerializerOptions SearchFactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
        var shapeCheckedResult = WithoutContradictorySuccessfulEmpty(tool, result);
        var publisherCheckedResult = WithoutUnprovenPublisherFacts(tool, shapeCheckedResult);
        var evidence = EvidenceOf(publisherCheckedResult, args);
        var (effectiveResult, publisherLimitations) =
            SplitPublisherLimitations(tool, publisherCheckedResult);
        UiEffect Finish(UiEffect effect) => WithEvidence(effect, evidence) with
        {
            PublisherLimitations = publisherLimitations.Count == 0
                ? null : publisherLimitations,
        };
        if (tool == "coverage") return Finish(CoverageResult(effectiveResult, locale));
        if (effectiveResult is JsonArray { Count: 0 })
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
                "search" => SearchWorkspace(args, effectiveResult),
                _ => new UiEffect(),
            };
            return Finish(empty);
        }
        var node = effectiveResult is JsonArray arr
            ? Aggregate(tool, arr) : effectiveResult as JsonObject;
        if (node is null) return new UiEffect();
        var status = LegalOperationPolicy.StatusForPublisherResult(node);
        var outcome = status is null ? (LegalOutcome?)null : LegalOperationPolicy.OutcomeForStatus(status);

        // A refusal is a first-class view: say what is missing and what does exist instead.
        if (status is not null && outcome is LegalOutcome.NeedsClarification
                or LegalOutcome.NotAvailable or LegalOutcome.NotFound or LegalOutcome.NotComparable)
        {
            var typedGaps = tool == "as_of" ? TypedProvisionGaps(node) : null;
            var explanation = tool == "as_of"
                && status == McpStatus.TextNotAvailable
                && S(node, "text_completeness") == "partial"
                && typedGaps is { Count: > 0 }
                    ? locale == "fr"
                        ? "Lex détient cette notice éditeur et un libellé certifié pour d'autres coordonnées, mais aucun libellé certifié n'est disponible pour la ou les coordonnées demandées."
                        : "Lex holds this publisher record and certified wording for other coordinates, but no certified wording is available for the requested coordinate or coordinates."
                    : Explain(status, locale, tool);
            var gap = new UiEffect(Gap: new GapView(
                Status: status,
                Work: CanonicalWork(node, args),
                Date: S(args, "date") ?? S(args, "as_of"),
                Explanation: explanation,
                Available: GapChoices(tool, node),
                ProvisionGaps: typedGaps,
                TotalProvisionGaps: tool == "as_of"
                    ? node["total_provision_gaps"]?.GetValue<int?>()
                    : null,
                Truncated: tool == "as_of"
                    && node["truncated"]?.GetValue<bool>() == true,
                TotalProvisions: tool == "as_of"
                    ? node["total_provisions"]?.GetValue<int?>()
                    : null,
                TextTruncated: tool == "as_of"
                    && node["text_truncated"]?.GetValue<bool>() == true,
                TextCompleteness: tool == "as_of"
                    ? S(node, "text_completeness")
                    : null));
            var refused = outcome == LegalOutcome.NotComparable && tool == "diff"
                ? UiEffect.Merge([Diff(node, args), gap])
                : gap;
            return Finish(refused);
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
            "search" => SearchWorkspace(args, effectiveResult),
            _ => new UiEffect(),
        };
        return Finish(mapped);
    }

    private static JsonNode WithoutContradictorySuccessfulEmpty(string tool, JsonNode result)
    {
        static bool IsContradictory(string operation, JsonObject item)
        {
            var status = LegalOperationPolicy.StatusForPublisherResult(item);
            return status is not null
                   && LegalOperationPolicy.OutcomeForStatus(status) == LegalOutcome.SucceededEmpty
                   && !LegalOperationPolicy.IsProvenSuccessfulPublisherResult(item, operation);
        }

        if (result is JsonObject single)
        {
            if (IsContradictory(tool, single))
                throw new InvalidDataException(
                    $"Tool '{tool}' returned rows or counts that contradict its empty status.");
            return result;
        }
        if (result is not JsonArray array) return result;

        var contradictory = array.OfType<JsonObject>()
            .Where(item => IsContradictory(tool, item))
            .ToHashSet();
        if (contradictory.Count == 0) return result;
        var retained = new JsonArray(array
            .Where(item => item is not JsonObject obj || !contradictory.Contains(obj))
            .Select(item => item?.DeepClone()).ToArray());
        if (!retained.OfType<JsonObject>().Any())
            throw new InvalidDataException(
                $"Tool '{tool}' returned no result with a proven execution shape.");
        return retained;
    }

    private static JsonNode WithoutUnprovenPublisherFacts(string tool, JsonNode result)
    {
        if (tool != "search") return result;

        static bool IsUnproven(string operation, JsonObject item)
        {
            if (!LegalOperationPolicy.HasPublisherEnvelope(item)
                || LegalOperationPolicy.IsProvenSuccessfulPublisherResult(item, operation))
                return false;
            var status = LegalOperationPolicy.StatusForPublisherResult(item);
            return status is null or LegalOperationStatus.PartialResult;
        }

        if (result is JsonObject single)
            return IsUnproven(tool, single) ? new JsonArray() : result;
        if (result is not JsonArray array) return result;
        var retained = array
            .Where(item => item is not JsonObject obj || !IsUnproven(tool, obj))
            .ToArray();
        return retained.Length == array.Count
            ? result
            : new JsonArray(retained.Select(item => item?.DeepClone()).ToArray());
    }

    /// <summary>
    /// A multi-publisher capability refusal is partial when another publisher answered. Retain
    /// the successful payload as the primary mapping and carry every refusal as a bounded typed
    /// disclosure. All-refusal payloads remain the existing full gap.
    /// </summary>
    private static (JsonNode Result, IReadOnlyList<PublisherLimitationView> Limitations)
        SplitPublisherLimitations(string tool, JsonNode result)
    {
        if (result is not JsonArray array) return (result, []);
        if (!string.Equals(LegalOperationPolicy.StatusForResult(result),
                LegalOperationStatus.PartialResult, StringComparison.Ordinal))
            return (result, []);
        var parts = array.OfType<JsonObject>().ToArray();
        var refusals = parts.Where(part => string.Equals(
                S(part["envelope"] as JsonObject, "status") ?? S(part, "status"),
                McpStatus.FilterNotSupportedByIndex,
                StringComparison.Ordinal))
            .ToArray();
        if (refusals.Length == 0 || refusals.Length == parts.Length)
            return (result, []);

        var supported = parts.Where(part =>
                LegalOperationPolicy.IsProvenSuccessfulPublisherResult(part, tool))
            .ToArray();
        if (supported.Length == 0)
            return (refusals[0].DeepClone(), []);
        var limitations = PublisherLimitationPolicy.FromResult(tool, result);
        return (new JsonArray(supported
            .Select(part => (JsonNode)part.DeepClone()).ToArray()), limitations);
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
        // Zero coverage rows can never be drawn as an inventory. CoverageView([]) is summed into
        // "Lex mounts 0 works and 0 verified versions", a false statement about the product's own
        // holdings, and coverage is the one tool whose job is to say what is NOT held. Whatever
        // emptied the payload — an unmatched filter, a stripped result — it is a gap, not a zero.
        if (rows.Count == 0)
            return new UiEffect(Gap: new GapView(
                McpStatus.NoResult, null, null, Explain(McpStatus.NoResult, locale, "coverage"), []));
        var status = LegalOperationPolicy.StatusForResult(result);
        var outcome = LegalOperationPolicy.OutcomeForStatus(status);
        if (outcome is LegalOutcome.NotAvailable or LegalOutcome.NotFound)
            return new UiEffect(Gap: new GapView(
                status, null, null, Explain(status, locale, "coverage"), []));
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
           || o["provision_gaps"] is JsonArray { Count: > 0 }
           || o["changes"] is JsonArray { Count: > 0 } || o["works"] is JsonArray { Count: > 0 }
           || o["hits"] is JsonArray { Count: > 0 }
           || o["document"] is JsonObject || o["from"] is JsonObject;

    /// <summary>
    /// Corpus-wide tools return one envelope per mounted publisher. A UI effect is one view, so
    /// selecting the first non-empty envelope silently turned an EU + Luxembourg answer into
    /// whichever index happened to be enumerated first. Combine only the explicitly aggregate
    /// tool shapes and retain each row's jurisdiction before mapping the view.
    /// </summary>
    private static JsonObject? Aggregate(string tool, JsonArray result)
    {
        static bool? SharedBoolean(IReadOnlyList<JsonObject> values, string field)
        {
            var facts = values.Select(value => B(value, field)).ToArray();
            return facts.Length > 0 && facts[0] is not null
                && facts.All(fact => fact == facts[0]) ? facts[0] : null;
        }

        var parts = result.OfType<JsonObject>().ToList();
        if (parts.Count == 0) return null;
        var aggregateStatus = LegalOperationPolicy.StatusForResult(result);
        var statusMatch = parts.FirstOrDefault(part => string.Equals(
            LegalOperationPolicy.StatusForPublisherResult(part),
            aggregateStatus,
            StringComparison.Ordinal));
        if (tool is not ("changes_in_period" or "in_force_on" or "cited_by"))
            return statusMatch ?? parts.FirstOrDefault(HasContent) ?? parts[0];

        var combined = (statusMatch ?? parts.FirstOrDefault(HasContent) ?? parts[0])
            .DeepClone().AsObject();
        if (combined["envelope"] is not JsonObject combinedEnvelope)
        {
            combinedEnvelope = new JsonObject();
            combined["envelope"] = combinedEnvelope;
        }
        combinedEnvelope["status"] = aggregateStatus;
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
        if (tool == "changes_in_period"
            && rows.OfType<JsonObject>().All(row => row["global_rank"] is JsonValue))
            rows = new JsonArray(rows.OfType<JsonObject>()
                .OrderBy(row => row["global_rank"]!.GetValue<int>())
                .Select(row => (JsonNode)row.DeepClone()).ToArray());
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
        {
            combined["total_works_in_force"] = parts.Sum(p => p["total_works_in_force"]?.GetValue<int>() ?? 0);
            var ambiguous = new JsonArray(parts
                .SelectMany(part => (part["ambiguous_works"] as JsonArray)
                    ?.OfType<JsonObject>() ?? [])
                .Take(20)
                .Select(item => (JsonNode)item.DeepClone()).ToArray());
            if (ambiguous.Count > 0) combined["ambiguous_works"] = ambiguous;
        }
        else
        {
            combined["citing_articles"] = rows.Count;
            const string evidenceScope =
                "captured_cross_references_in_held_non_withdrawn_versions";
            combined["evidence_scope"] = parts.All(part =>
                string.Equals(S(part, "evidence_scope"), evidenceScope, StringComparison.Ordinal))
                    ? evidenceScope : null;
            combined["current_legal_effect_assessed"] =
                SharedBoolean(parts, "current_legal_effect_assessed");
            combined["relationship_type_assessed"] =
                SharedBoolean(parts, "relationship_type_assessed");

            var truncation = parts.Select(part =>
                B(part["response_row_set"] as JsonObject, "truncated")).ToArray();
            var aggregateTruncation = truncation.Any(value => value == true)
                ? true
                : truncation.All(value => value == false) ? false : (bool?)null;
            var receipt = combined["response_row_set"] as JsonObject ?? new JsonObject();
            receipt["truncated"] = aggregateTruncation;
            combined["response_row_set"] = receipt;
        }
        return combined;
    }

    private static UiEffect Provision(JsonObject o, JsonObject args)
    {
        var doc = o["document"] as JsonObject ?? o;
        var provisionGaps = TypedProvisionGaps(o);
        var items = (o["provisions"] as JsonArray)?.OfType<JsonObject>()
            .Select(p => new ProvisionItem(
                Anchor: S(p, "anchor") ?? "",
                Num: S(p, "num"), Heading: S(p, "heading"),
                Text: S(p, "text") ?? S(p, "text_md") ?? "",
                Sha: S(p, "text_sha256"),
                TextOmitted: p["text_omitted"]?.GetValue<bool>() == true,
                TextOmittedReason: S(p, "text_omitted_reason"),
                Permalink: S(p, "permalink"),
                DocumentOrder: p["document_order"]?.GetValue<int?>())).Where(i => i.Text.Length > 0
                    || i.Anchor.Length > 0 || !string.IsNullOrWhiteSpace(i.Heading)).ToList()
            ?? [];
        if (items.Count == 0 && S(doc, "text") is { Length: > 0 } documentText)
            items.Add(new ProvisionItem("", null, S(doc, "title"), documentText, null));
        if (items.Count == 0 && doc["text_omitted"]?.GetValue<bool>() == true)
            items.Add(new ProvisionItem("", null, S(doc, "title"), "", null,
                TextOmitted: true,
                TextOmittedReason: S(doc, "text_omitted_reason"),
                Permalink: S(doc, "permalink") ?? S(doc, "source_uri")));
        if (items.Count == 0 && provisionGaps.Count == 0) return new UiEffect();
        return new UiEffect(Provision: new ProvisionView(
            Subject: SubjectOf(doc, args),
            ValidFrom: S(doc, "valid_from") ?? "",
            ValidTo: S(doc, "valid_to"),
            Provisions: items,
            Permalink: S(doc, "permalink"),
            TotalProvisions: o["total_provisions"]?.GetValue<int?>(),
            Truncated: o["truncated"]?.GetValue<bool>() ?? false,
            TextTruncated: o["text_truncated"]?.GetValue<bool>() ?? false,
            OutlineOnly: S(args, "mode") == "outline",
            ProvisionGaps: provisionGaps,
            TotalProvisionGaps: o["total_provision_gaps"]?.GetValue<int?>(),
            TextCompleteness: S(o, "text_completeness")));
    }

    private static IReadOnlyList<ProvisionGapItem> TypedProvisionGaps(JsonObject result) =>
        (result["provision_gaps"] as JsonArray)?.OfType<JsonObject>()
            .Select(gap => new ProvisionGapItem(
                Anchor: S(gap, "anchor") ?? "",
                DocumentOrder: gap["document_order"]?.GetValue<int>() ?? 0,
                Num: S(gap, "num"),
                Heading: S(gap, "heading"),
                Path: S(gap, "path"),
                ArticleValidFrom: S(gap, "article_valid_from"),
                TextUnavailableReason: S(gap, "text_unavailable_reason")
                    ?? "text_not_available",
                OfficialSource: S(gap, "official_source"),
                Eli: S(gap, "eli"),
                SourceUri: S(gap, "source_uri")))
            .Where(gap => gap.Anchor.Length > 0)
            .OrderBy(gap => gap.DocumentOrder)
            .ToArray()
        ?? [];

    private static UiEffect History(JsonObject o, JsonObject args)
    {
        if (o["states"] is not JsonArray states || states.Count == 0) return new UiEffect();
        return new UiEffect(History: new HistoryView(
            Subject: new Subject(CanonicalWork(o, args), null, null, S(o, "anchor"),
                S(o, "language") ?? S(args, "language")),
            Anchor: S(o, "anchor") ?? "",
            DistinctTexts: o["distinct_texts"]?.GetValue<int>() ?? states.Count,
            States: states.OfType<JsonObject>().Select(s => new HistoryState(
                S(s, "valid_from") ?? "", S(s, "valid_to"), S(s, "text_sha256"), S(s, "permalink"))).ToList(),
            Truncated: B(o, "truncated")));
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
                S(latest, "language") ?? S(args, "language")),
            Rows: rows.Select(version => new TimelineState(
                S(version, "lex_id"),
                S(version, "valid_from") ?? "",
                S(version, "valid_to"),
                S(version, "title"),
                S(version, "language"),
                S(version, "permalink"),
                S(version, "record_sha256"))).ToList(),
            TotalCount: o["total_count"]?.GetValue<int>() ?? rows.Count,
            Truncated: B(o, "truncated")));
    }

    private static UiEffect Diff(JsonObject o, JsonObject args)
    {
        var from = S(args, "from_date") ?? S(o, "from_date");
        var to = S(args, "to_date") ?? S(o, "to_date");
        if (from is null || to is null) return new UiEffect();
        // diff returns the two resolved documents as `from` / `to`, not a list.
        var a = o["from"] as JsonObject;
        var b = o["to"] as JsonObject;
        var comparisonLimitations = Strings(
            o, "comparison_limitations", out var comparisonLimitationsMalformed);
        return new UiEffect(Diff: new DiffView(
            Subject: new Subject(CanonicalWork(o, args),
                S(b, "title") ?? S(a, "title"), from, S(o, "anchor"),
                S(b, "language") ?? S(a, "language") ?? S(args, "language")),
            FromDate: from, ToDate: to,
            FromPermalink: S(a, "permalink"), ToPermalink: S(b, "permalink"),
            Note: S(o, "note"),
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status"),
            AnchorFromPresent: o["anchor_from_present"]?.GetValue<bool?>(),
            AnchorToPresent: o["anchor_to_present"]?.GetValue<bool?>(),
            AnchorTextEqual: o["anchor_text_equal"]?.GetValue<bool?>(),
            // Both were GetValue calls, which throw on a string or a number and lose the whole
            // typed result to one malformed field. Same boundary, same rule as the receipt
            // readers: not exactly true or false means no claim.
            ProvisionLevelComparable: B(o, "provision_level_comparable") ?? false,
            Changed: B(o, "changed"),
            ComparisonLimitations: comparisonLimitations,
            ComparisonLimitationsMalformed: comparisonLimitationsMalformed));
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
            // Both spellings of the one instant, as the gap view above already reads them. A
            // point-in-time search carries it as as_of, so reading only date landed the reader on
            // today's law with the controls silently disagreeing with the answer above them.
            Date: S(args, "date") ?? S(args, "as_of"),
            Anchor: S(args, "anchor"));
        return view is { Query: null, Jurisdiction: null, Hierarchy: null, Domain: null, SourceClass: null,
                         ActForm: null, BindingStatus: null, Page: null, Language: null,
                         Work: null, Date: null, Anchor: null }
            ? new UiEffect()
            : new UiEffect(Workspace: view);
    }

    private static UiEffect SearchWorkspace(JsonObject args, JsonNode result)
    {
        var workspace = Workspace(args).Workspace;
        if (workspace is null) return new UiEffect();
        var facts = new List<SearchFact>(8);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = result is JsonArray array
            ? array.OfType<JsonObject>()
            : result is JsonObject item ? [item] : [];
        foreach (var hit in rows.SelectMany(row =>
                     (row["hits"] as JsonArray)?.OfType<JsonObject>() ?? []))
        {
            if (facts.Count == 8) break;
            var lexId = S(hit, "lex_id");
            var anchor = S(hit, "anchor");
            var work = WorkOf(lexId);
            if (lexId is null || work is null || anchor is null
                || lexId.Length > LegalOperationCatalog.MaximumStringLength
                || anchor.Length > LegalOperationCatalog.MaximumAnchorLength)
                continue;
            var identity = $"{lexId}#{anchor}";
            if (seen.Contains(identity)) continue;
            var fact = new SearchFact(
                Work: work,
                LexId: lexId,
                Anchor: anchor,
                Number: Bounded(S(hit, "provision_num"), 64),
                Heading: Bounded(S(hit, "provision_heading"), 160),
                Snippet: Bounded(S(hit, "snippet"), 240),
                Title: Bounded(S(hit, "title"), 200),
                ValidFrom: Within(S(hit, "valid_from"), 10),
                SourceUri: Within(S(hit, "source_uri"), 384),
                Permalink: Within(S(hit, "permalink"), 384));
            facts.Add(fact);
            if (JsonSerializer.Serialize(facts, SearchFactJson).Length
                > MaximumSearchFactsJsonCharacters)
                facts.RemoveAt(facts.Count - 1);
            else
                seen.Add(identity);
        }
        return new UiEffect(Workspace: workspace with { Results = facts });
    }

    private static string? Bounded(string? value, int maximum) => value switch
    {
        null => null,
        { Length: var length } when length <= maximum => value,
        _ => value[..(maximum - 1)] + "…",
    };

    private static string? Within(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum ? value : null;

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
            Status: S(o["envelope"] as JsonObject, "status") ?? S(o, "status"),
            // The receipt is stamped into every item of the response (McpCore
            // MarkResponseRows), so reading it from this unit reads the response-wide fact.
            // Absent stays null rather than becoming false: a missing receipt is not
            // evidence of a complete answer.
            RowsTruncated: B(o["response_row_set"] as JsonObject, "truncated"),
            EvidenceScope: S(o, "evidence_scope"),
            CurrentLegalEffectAssessed: B(o, "current_legal_effect_assessed"),
            RelationshipTypeAssessed: B(o, "relationship_type_assessed")));
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
                Permalink: S(c, "permalink"), DiffPermalink: S(c, "diff_permalink"),
                GlobalRank: c["global_rank"]?.GetValue<int?>())).ToList(),
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

    private static IReadOnlyList<string> GapChoices(string tool, JsonObject result)
    {
        const int maximum = 20;
        var choices = new List<string>(maximum);
        void Add(JsonArray? source, string name, string? work = null)
        {
            foreach (var choice in source?.OfType<JsonObject>() ?? [])
            {
                if (choices.Count >= maximum) return;
                if (S(choice, "version_key") is not { Length: > 0 } key
                    || key.Length > LegalOperationCatalog.MaximumVersionKeyLength) continue;
                choices.Add(work is null ? $"{name}={key}" : $"{work}: {name}={key}");
            }
        }

        Add(result["version_choices"] as JsonArray, "version_key");
        Add(result["from_version_choices"] as JsonArray, "from_version_key");
        Add(result["to_version_choices"] as JsonArray, "to_version_key");
        foreach (var ambiguous in (result["ambiguous_works"] as JsonArray)
                     ?.OfType<JsonObject>() ?? [])
        {
            Add(ambiguous["choices"] as JsonArray, "version_key", S(ambiguous, "work"));
            if (choices.Count >= maximum) break;
        }
        if (choices.Count > 0) return choices;

        if (result["versions"] is JsonArray versions)
            return versions.OfType<JsonObject>().Select(version => S(version, "valid_from") ?? "")
                .Where(value => value.Length > 0).Take(12).ToList();
        if (result["anchors_not_in_version"] is JsonArray anchors)
            return anchors.Select(anchor => anchor?.GetValue<string>() ?? "")
                .Where(value => value.Length > 0).Take(20).ToList();
        return [];
    }

    private static string Explain(string status, string locale, string tool)
    {
        if (locale == "fr")
            return status switch
            {
                McpStatus.NoCorpusMounted => "Lex ne dispose d'aucun index juridique vérifié, donc aucune opération juridique n'est disponible.",
                McpStatus.RetrievalModeUnavailable => "La recherche par sens n'est pas disponible pour ce périmètre, car son benchmark de recherche signé n'en a pas autorisé l'activation. La recherche par mots exacts reste disponible.",
                McpStatus.FilterNotSupportedByIndex => "L'index signé ne prend pas en charge ce filtre pour la langue et la période demandées. Retirez le filtre ou consultez la couverture déclarée.",
                McpStatus.NoVersionForDate => "Lex détient cet instrument, mais aucune version de l'éditeur ne couvre cette date.",
                McpStatus.AmbiguousVersion when tool == "diff" => "L'éditeur expose plusieurs versions identifiées à une limite de comparaison. Choisissez une version exacte pour chaque limite ambiguë.",
                McpStatus.AmbiguousVersion => "L'éditeur expose plusieurs versions identifiées à cette date. Choisissez une version exacte de l'éditeur.",
                McpStatus.UnknownWork => "Lex ne détient pas cet instrument.",
                McpStatus.UnknownPublisher => "Aucun éditeur portant cet identifiant n'est monté ici. Reposez la question sans filtre d'éditeur pour voir tout ce que Lex détient.",
                McpStatus.UnknownAnchor => "Cet identifiant d'article n'existe pas dans cet instrument.",
                McpStatus.AnchorNotInVersion => "Cet article n'existait pas dans la version de l'éditeur sélectionnée pour cette date.",
                McpStatus.TextWithheld => "Lex détient cette version et son texte, mais une règle de publication empêche d'en servir le libellé.",
                McpStatus.TextNotAvailable => "Lex détient la notice et les dates de l'éditeur, mais aucun texte de disposition ne peut être servi de manière fiable.",
                McpStatus.NoProvisionHistory => "Lex détient cet instrument sans historique par article, donc un article isolé ne peut pas être suivi dans le temps.",
                McpStatus.ProfilesDiffer => "Lex détient les deux versions, mais leurs profils d'extraction ne permettent pas une comparaison fiable.",
                // Reached only by the zero-row coverage guard: an empty payload describes the
                // request, never the holdings.
                McpStatus.NoResult => "Cette demande n'a renvoyé aucune ligne de couverture, donc Lex ne peut rien affirmer sur ce périmètre. Reposez la question sans filtre pour voir tout ce qu'il détient.",
                _ => "Lex ne peut pas répondre à partir de ce qu'il détient.",
            };
        return status switch
        {
            McpStatus.NoCorpusMounted => "Lex has no verified legal index mounted, so no legal operation is available.",
            McpStatus.RetrievalModeUnavailable => "Meaning search is unavailable for this scope because its signed retrieval benchmark has not authorized activation. Exact-word search remains available.",
            McpStatus.FilterNotSupportedByIndex => "The signed index does not support this filter for the requested language and period. Remove the filter or inspect the declared coverage.",
            McpStatus.NoVersionForDate => "Lex holds this law, but no publisher version covers that date.",
            McpStatus.AmbiguousVersion when tool == "diff" => "The publisher exposes multiple identified versions at a comparison boundary. Choose one exact publisher version for each ambiguous comparison boundary.",
            McpStatus.AmbiguousVersion => "The publisher exposes multiple identified versions for that date. Choose one exact publisher version.",
            McpStatus.UnknownWork => "Lex does not hold this work at all.",
            McpStatus.UnknownPublisher => "No publisher with that id is mounted here. Ask again without a publisher filter to see everything Lex holds.",
            McpStatus.UnknownAnchor => "That article identifier does not exist in this law.",
            McpStatus.AnchorNotInVersion => "That article did not exist in the publisher version selected for that date.",
            McpStatus.TextWithheld => "Lex holds this version and its text, but a publication gate prevents serving the wording.",
            McpStatus.TextNotAvailable => "Lex holds this publisher record and dates, but no safely derived provision text is available.",
            McpStatus.NoProvisionHistory => "Lex holds this work without per-article history, so single articles cannot be traced through time.",
            McpStatus.ProfilesDiffer => "Lex holds both versions, but their extraction profiles do not support a reliable provision comparison.",
            McpStatus.NoResult => "This request returned no coverage rows, so Lex cannot state anything about that scope. Ask again without a filter to see everything it holds.",
            _ => "Lex cannot answer this from what it holds.",
        };
    }

    // Nullable on purpose: callers reach into optional sub-objects (`o["from"] as JsonObject`),
    // and a tool response that omits one of them must map to a missing field, not to a throw that
    // loses the whole answer along with its UI payload.
    private static string? S(JsonObject? o, string k)
        => o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// A JSON boolean, or null for everything else, in the same shape as <see cref="S"/>.
    ///
    /// GetValue&lt;bool&gt; throws on a string or a number, which would lose the entire typed
    /// operation result to one malformed field. The MCP result is an untrusted boundary, so a
    /// value that is not exactly true or false is not a fact: it degrades to no claim, never to
    /// an exception and never to false.
    /// </summary>
    private static bool? B(JsonObject? o, string k)
        => o?[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>
    /// The usable JSON strings in an array. A present malformed field is reported separately,
    /// while valid siblings survive, so damage can neither erase a real limitation nor hide.
    /// </summary>
    private static IReadOnlyList<string>? Strings(JsonObject? o, string k, out bool malformed)
    {
        malformed = false;
        if (o is null || !o.ContainsKey(k)) return null;
        if (o[k] is not JsonArray array)
        {
            malformed = true;
            return null;
        }
        var values = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
                values.Add(text);
            else
                malformed = true;
        }
        if (array.Count == 0) malformed = true;
        return values.Count > 0 ? values : null;
    }
}
