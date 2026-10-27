using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Api;

/// <summary>
/// R92 at serve time (the plan's item 1.11; the specification of record's R92(1) and (2), R64): the names record beside the
/// mount (<see cref="NamesRecord"/>, written by <c>Lex.V3.Tool names-records</c> from R92's measurement and carried by the
/// release beside the mount) lists the works whose class or served text names private individuals. Lex points to the
/// publisher for each: every operation that would show its text or titles answers <c>text_withheld</c> with the reason
/// <c>names_private_individuals</c>, its identity, its hashes and its official link; search never matches its text nor shows
/// its titles; a citing article of it is not quoted in another act's <c>cited_by</c>. Its identity, hashes and private custody
/// stay (R92(2)): <c>browse</c>, <c>coverage</c>, <c>events</c> and <c>changes_in_period</c> still name it by its identifiers,
/// which carry no text.
/// </summary>
/// <remarks>
/// Applied when the mount serves, from a record beside it, rather than in the derivation: the measurement reads the derived
/// indexes (it needs no new derive and keeps derivation byte-stable, the launch contract's two-build check), a withdrawal is a
/// new release with a new record (R92(2)), and the index holds the text in the private image only, never in a public asset
/// (R92(3)). The record is bound to the mount's corpus and index digests: a record of another build, or one that breaks a
/// rule, refuses the mount whole (fail closed), so a release cannot serve a build its measurement did not read.
/// </remarks>
internal sealed partial class V3CorpusMount
{
    internal const string NamesWithheldRule =
        "R92: this work names private individuals, by its class or by what R92's measurement found in its served text or titles; Lex " +
        "points to the publisher: its text and titles are not served, searched, quoted or exported, it is kept out of search engines, " +
        "and its identity, hashes and official link stay";

    /// <summary>The operations whose answers list many works: their rows are held to the record where they would carry text, never refused whole.</summary>
    private static readonly HashSet<string> NamesListOperations = new(StringComparer.Ordinal)
    {
        "answer_drift", "ask", "browse", "changes_in_period", "coverage", "events",
    };

    /// <summary>The works a mount's names record withholds, by every identity an answer names them by.</summary>
    private sealed class NamesWithheld
    {
        private readonly Dictionary<string, NamesWithheldWork> _byWorkKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NamesWithheldWork> _byLuxembourgIri = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NamesWithheldWork> _byCelex = new(StringComparer.Ordinal);
        private readonly HashSet<string> _luxembourgStates = new(StringComparer.Ordinal);

        public NamesWithheld(IReadOnlyList<NamesWithheldWork> works, string? recordSha256)
        {
            Works = works;
            RecordSha256 = recordSha256;
            foreach (var work in works)
            {
                if (string.Equals(work.Publisher, NamesRecord.EuropePublisher, StringComparison.Ordinal))
                {
                    _byCelex[work.Celex!] = work;
                    continue;
                }

                _byLuxembourgIri[work.WorkIri] = work;
                if (work.WorkKey is not null)
                {
                    _byWorkKey[work.WorkKey] = work;
                }

                _luxembourgStates.UnionWith(work.StateSha256s);
            }
        }

        internal IReadOnlyList<NamesWithheldWork> Works { get; }

        /// <summary>The SHA-256 of the record's bytes (a refusal's content digest only when the work has no state digest); null with no record.</summary>
        internal string? RecordSha256 { get; }

        internal bool Any => Works.Count > 0;

        internal NamesWithheldWork? ByWorkKey(string? workKey) =>
            workKey is not null && _byWorkKey.TryGetValue(workKey, out var work) ? work : null;

        /// <summary>A Luxembourg work by its work IRI or any IRI under it (its Journal officiel resource, an expression, a consolidation).</summary>
        internal NamesWithheldWork? ByLuxembourgIri(string? iri)
        {
            if (iri is null || _byLuxembourgIri.Count == 0)
            {
                return null;
            }

            if (_byLuxembourgIri.TryGetValue(iri, out var work))
            {
                return work;
            }

            return PrivateIndividuals.LuxembourgWorkIriOf(iri) is { } workIri && _byLuxembourgIri.TryGetValue(workIri, out work) ? work : null;
        }

        internal NamesWithheldWork? BySeedCelex(string? celex) =>
            celex is not null && _byCelex.TryGetValue(celex, out var work) ? work : null;

        /// <summary>Whether a Luxembourg state belongs to a withheld work (search never matches it).</summary>
        internal bool HoldsLuxembourgState(string stateSha256) => _luxembourgStates.Contains(stateSha256);

        internal bool AnyEurope => _byCelex.Count > 0;
    }

    /// <summary>
    /// The names record beside the mount in <paramref name="directory"/>, held to the mount's corpus and index digests, or
    /// a record of none when there is none. A record that breaks a rule throws, and the mount is refused.
    /// </summary>
    private static async Task<NamesWithheld> ReadNamesRecordAsync(
        string directory,
        string corpusSha256,
        LuxembourgIndexReader? reader,
        Lex.V3.Ingest.Europe.EuropeIndexReader? europeReader,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, NamesRecord.FileName);
        if (!File.Exists(path))
        {
            return new NamesWithheld([], null);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var works = NamesRecord.Read(bytes, new NamesMeasuredMount(corpusSha256, reader?.IndexRef.Sha256, europeReader?.IndexRef.Sha256, null));
        return new NamesWithheld(works, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>Whether this mount withholds any work under R92 (none without a names record).</summary>
    internal bool WithholdsUnderR92 => _names.Any;

    /// <summary>
    /// <paramref name="outcome"/>, or <c>text_withheld</c> when it is an answer about a work the names record withholds: one whose
    /// own identity (its work key, its publisher's work or legal-resource IRI, its CELEX, the identifier asked) is the withheld
    /// work's. An answer listing many works is left to the rows' own rules (<see cref="NamesListOperations"/>); a refusal carries
    /// no text and is left as it is.
    /// </summary>
    internal V3PlatformOperationOutcome WithNamesWithheld(
        V3PlatformOperationRequest request,
        V3PlatformOperationOutcome outcome,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcome);
        if (!_names.Any || outcome.Result is not { } result || NamesListOperations.Contains(request.OperationId))
        {
            return outcome;
        }

        var value = result.Value;
        var work = WithheldWorkNamedBy(value);
        return work is null ? outcome : RefuseNamesWithheld(request, work, value, observedAt);
    }

    /// <summary>The withheld work an answer is about, by the identities at its top level, or null.</summary>
    private NamesWithheldWork? WithheldWorkNamedBy(JsonElement value)
    {
        string? Member(string name) =>
            value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String ? member.GetString() : null;

        return _names.ByWorkKey(Member("work_key"))
            ?? _names.ByLuxembourgIri(Member("publisher_work_iri"))
            ?? _names.ByLuxembourgIri(Member("publisher_legal_resource_iri"))
            ?? WithheldEurope(Member("celex"))
            ?? WithheldEurope(Member("work_celex"))
            ?? WithheldEurope(Member("publisher_work_id"))
            ?? WithheldNamedByIdentifier(Member("requested_identifier"));
    }

    /// <summary>The withheld EU act an identity names (its CELEX, a version's CELEX or work, an expression), or null.</summary>
    private NamesWithheldWork? WithheldEurope(string? identity)
    {
        if (identity is null || !_names.AnyEurope)
        {
            return null;
        }

        return _names.BySeedCelex(identity) ?? EuropeSeedsOf(identity).Select(_names.BySeedCelex).FirstOrDefault(static work => work is not null);
    }

    /// <summary>
    /// The withheld work an identifier as asked names: a site address or a permalink (<c>/lu-legilux/{work}/…</c>,
    /// <c>/eu-eurlex/{celex}/…</c>), a work key, an ELI or an EU identity; null for anything else (a title is read by its answer).
    /// </summary>
    internal NamesWithheldWork? WithheldNamedByIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !_names.Any)
        {
            return null;
        }

        var path = identifier.Split('#', '?')[0];
        foreach (var (prefix, europe) in new[] { ("/lu-legilux/", false), ("/eu-eurlex/", true) })
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                var segment = Uri.UnescapeDataString(path[prefix.Length..].Split('/')[0]);
                return europe ? WithheldEurope(segment) : _names.ByWorkKey(segment);
            }
        }

        return _names.ByWorkKey(identifier) ?? _names.ByLuxembourgIri(identifier) ?? WithheldEurope(identifier);
    }

    /// <summary>
    /// <c>text_withheld</c> for a work R92 withholds: the registry's three (its official identity, its official link, a content
    /// digest: the state the answer was about, else the work's first state digest), the reason, its class and reason codes, every
    /// state digest the index holds of it (R64: a withheld text answers with the hashes) and the rule. No title and no text.
    /// </summary>
    private V3PlatformOperationOutcome RefuseNamesWithheld(
        V3PlatformOperationRequest request,
        NamesWithheldWork work,
        JsonElement? answered,
        DateTimeOffset observedAt)
    {
        string? Answered(string name) =>
            answered is { } value && value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String ? member.GetString() : null;

        var europe = string.Equals(work.Publisher, NamesRecord.EuropePublisher, StringComparison.Ordinal);
        var stateDigest = Answered("state_sha256") ?? Answered("wording_sha256");
        using var withheld = JsonSerializer.SerializeToDocument(new
        {
            official_identity = europe
                ? $"http://publications.europa.eu/resource/celex/{work.Celex}"
                : Answered("publisher_legal_resource_iri") ?? work.WorkIri,
            official_link = work.WorkIri,
            content_sha256 = stateDigest ?? work.StateSha256s.FirstOrDefault() ?? _names.RecordSha256,
            reason = PrivateIndividuals.Reason,
            publisher = work.Publisher,
            work_key = work.WorkKey,
            celex = work.Celex,
            @class = work.Class,
            evidence = work.Evidence,
            state_sha256s = work.StateSha256s,
            rule = NamesWithheldRule,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, europe ? PublisherId.EuEurLex : PublisherId.LuLegilux),
            new V3PlatformOperationRefusal(request, "text_withheld", withheld.RootElement));
    }

    /// <summary>Whether search may match a Luxembourg state's text: never one of a withheld work.</summary>
    private bool SearchMatchesUnderR92(string stateSha256) => !_names.HoldsLuxembourgState(stateSha256);

    /// <summary>The title ladder's answer with every work R92 withholds dropped: a withheld title is never matched nor shown.</summary>
    private LuxembourgIndexWorkResolution TitleLadder(string title)
    {
        var resolution = _reader!.ResolveWorkTitle(title);
        if (!_names.Any || resolution.Candidates.Count == 0)
        {
            return resolution;
        }

        return resolution with
        {
            Candidates = resolution.Candidates
                .Where(candidate => !candidate.ExpressionIris.Any(expression => _names.ByLuxembourgIri(expression) is not null))
                .ToArray(),
        };
    }

    /// <summary>Whether R92 withholds a Luxembourg work by its key or IRI (its titles are then never served).</summary>
    private bool TitlesWithheld(string? workKey, string? workIri) =>
        _names.Any && (_names.ByWorkKey(workKey) is not null || _names.ByLuxembourgIri(workIri) is not null);

    /// <summary>Whether a citing state belongs to a withheld work (another act's <c>cited_by</c> never quotes it).</summary>
    private bool CitingWithheld(LuxembourgIndexResolvedState state) =>
        _names.Any && (_names.ByWorkKey(state.WorkKey) is not null || _names.HoldsLuxembourgState(state.StateSha256));
}
