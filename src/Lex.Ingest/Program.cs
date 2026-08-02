using Lex.Index;
using Lex.Ingest;
using Lex.Law;
using Lex.Sources.Legilux;

var args0 = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (args0.Length == 0) { Usage(); return 1; }

string? Get(string name)
{
    var i = Array.IndexOf(args0, name);
    return i >= 0 && i + 1 < args0.Length ? args0[i + 1] : null;
}

// Time enters as an injected parameter (F9); the wall clock is read only at this CLI boundary.
var now = Get("--now") is { } n ? DateTimeOffset.Parse(n) : DateTimeOffset.UtcNow;

switch (args0[0])
{
    case "ingest":
    {
        var publisher = Get("--publisher") ?? "lu-legilux";
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        ISourceAdapter adapter = publisher switch
        {
            "lu-legilux" => new LegiluxAdapter(),
            "eu-eurlex" => new Lex.Sources.EurLex.EurLexAdapter(),
            _ => throw new ArgumentException($"Unknown publisher '{publisher}'"),
        };
        Console.Error.WriteLine($"[lex] ingest {publisher} -> {corpus}");
        await new CorpusWriter(corpus, now).WriteAsync(adapter, CancellationToken.None);
        return 0;
    }
    case "index":
    {
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var articles = Get("--articles");
        var outDb = Get("--out") ?? throw new ArgumentException("--out required");
        var keyFile = Get("--keyfile");
        string? keyPem = null;
        if (keyFile is not null)
        {
            if (!File.Exists(keyFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
                File.WriteAllText(keyFile, StampSigner.CreateKeyPem());
                Console.Error.WriteLine($"[lex] generated signing key at {keyFile}");
            }
            keyPem = File.ReadAllText(keyFile);
        }
        Console.Error.WriteLine($"[lex] index {corpus} (articles: {articles ?? "none"}) -> {outDb}");
        IndexFromCorpus.Build(corpus, articles, outDb, keyPem, now);
        return 0;
    }
    case "derive":
    {
        var publisher = Get("--publisher") ?? "lu-legilux";
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var outRoot = Get("--out") ?? throw new ArgumentException("--out required");
        Console.Error.WriteLine($"[lex] derive {publisher} {corpus} -> {outRoot}");
        var stats = Lex.Derive.DeriveWriter.Derive(corpus, outRoot, publisher);
        Console.Error.WriteLine($"  [derive] works={stats.Works} versions={stats.Versions} provisions={stats.Provisions} skipped={stats.Skipped} errors={stats.Errors.Count}");
        foreach (var e in stats.Errors.Take(20)) Console.Error.WriteLine($"  [derive] ERROR {e}");
        return stats.Errors.Count == 0 ? 0 : 2;
    }
    case "verify":
    {
        // verify stamp --db X   |   verify derive --publisher P --corpus X --articles Y [--work slug]
        switch (args0.Length > 1 ? args0[1] : "")
        {
            case "stamp":
            {
                var db = Get("--db") ?? throw new ArgumentException("--db required");
                using var r = Lex.Index.LexIndexReader.Open(db);
                Console.WriteLine($"collection={r.Collection} schema={r.Stamp.GetValueOrDefault("schema")} " +
                    $"algorithm={r.Stamp.GetValueOrDefault("algorithm")} corpus_commit={r.Stamp.GetValueOrDefault("corpus_commit")} " +
                    $"built_at={r.Stamp.GetValueOrDefault("built_at")} signature_valid={r.SignatureValid}");
                return r.SignatureValid ? 0 : 3;
            }
            case "derive":
            {
                var publisher = Get("--publisher") ?? "lu-legilux";
                var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
                var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
                var onlyWork = Get("--work");
                var tmp = Path.Combine(Path.GetTempPath(), $"lex-verify-{Guid.NewGuid():N}");
                try
                {
                    // re-derive (optionally one work via a filtered shadow corpus) and byte-compare
                    var corpusToUse = corpus;
                    if (onlyWork is not null)
                    {
                        corpusToUse = Path.Combine(tmp, "corpus");
                        Directory.CreateDirectory(Path.Combine(corpusToUse, "works"));
                        File.Copy(Path.Combine(corpus, "manifest.json"), Path.Combine(corpusToUse, "manifest.json"));
                        CopyDir(Path.Combine(corpus, "works", onlyWork), Path.Combine(corpusToUse, "works", onlyWork));
                    }
                    var outDir = Path.Combine(tmp, "articles");
                    Lex.Derive.DeriveWriter.Derive(corpusToUse, outDir, publisher);
                    int compared = 0, mismatched = 0, missing = 0;
                    foreach (var f in Directory.EnumerateFiles(Path.Combine(outDir, publisher), "*.*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(outDir, f);
                        var published = Path.Combine(articles, rel);
                        compared++;
                        if (!File.Exists(published)) { missing++; Console.Error.WriteLine($"MISSING in published layer: {rel}"); }
                        else if (!File.ReadAllBytes(f).SequenceEqual(File.ReadAllBytes(published)))
                        { mismatched++; Console.Error.WriteLine($"MISMATCH: {rel}"); }
                    }
                    Console.WriteLine($"verify derive: compared={compared} mismatched={mismatched} missing={missing}");
                    return mismatched == 0 && missing == 0 ? 0 : 3;
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }
            }
            default:
                Console.Error.WriteLine("usage: lex verify stamp --db X | lex verify derive --publisher P --corpus X --articles Y [--work slug]");
                return 1;
        }
    }
    case "dataset":
    {
        // One JSON line per provision-version, per publisher: the AI-builder consumption file.
        var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
        var outDir = Get("--out") ?? throw new ArgumentException("--out required");
        Directory.CreateDirectory(outDir);
        foreach (var pubDir in Directory.EnumerateDirectories(articles).Where(d => Directory.Exists(Path.Combine(d, "works"))).OrderBy(d => d, StringComparer.Ordinal))
        {
            var pub = Path.GetFileName(pubDir);
            var outPath = Path.Combine(outDir, $"{pub}-provisions.jsonl.gz");
            var rowCount = 0;
            await using var fs = File.Create(outPath);
            await using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
            await using var w = new StreamWriter(gz, new System.Text.UTF8Encoding(false));
            foreach (var jf in Directory.EnumerateFiles(pubDir, "*.json", SearchOption.AllDirectories)
                         .Where(f => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))!) == "versions")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jf));
                var root = doc.RootElement;
                string? S2(System.Text.Json.JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                foreach (var p in root.GetProperty("provisions").EnumerateArray())
                {
                    var line = new System.Text.Json.Nodes.JsonObject
                    {
                        ["provision_id"] = S2(p, "provision_id"),
                        ["lex_id"] = S2(root, "lex_id"),
                        ["anchor"] = S2(p, "anchor"),
                        ["type"] = S2(p, "type"),
                        ["num"] = S2(p, "num"),
                        ["heading"] = S2(p, "heading"),
                        ["language"] = S2(root, "language"),
                        ["valid_from"] = S2(root, "valid_from"),
                        ["valid_to"] = S2(root, "valid_to"),
                        ["article_valid_from"] = S2(p, "article_valid_from"),
                        ["title"] = S2(root, "title"),
                        ["text_md"] = S2(p, "text_md"),
                        ["text_sha256"] = S2(p, "text_sha256"),
                        ["source_sha256"] = S2(root.GetProperty("derived_from"), "sha256"),
                        ["source_uri"] = S2(root.GetProperty("derived_from"), "source_uri"),
                        ["profile"] = S2(root.GetProperty("generator"), "profile"),
                        ["license"] = S2(root, "license"),
                        ["attribution"] = S2(root, "attribution"),
                    };
                    await w.WriteLineAsync(line.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                    rowCount++;
                }
            }
            Console.Error.WriteLine($"  [dataset] {outPath}: {rowCount} provision rows");
        }
        return 0;
    }
    case "catalog":
    {
        var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
        var s = Lex.Derive.CatalogBuilder.Build(articles);
        Console.Error.WriteLine($"  [catalog] works={s.Works} anchors={s.Anchors} history_states={s.HistoryStates}");
        return 0;
    }
    default:
        Usage();
        return 1;
}

static void CopyDir(string src, string dst)
{
    Directory.CreateDirectory(dst);
    foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
    foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
}

static void Usage() => Console.Error.WriteLine("""
    lex — point-in-time regulatory text pipeline
      lex ingest --publisher lu-legilux --corpus PATH [--now ISO]
      lex index  --corpus PATH [--articles PATH] --out FILE.db [--keyfile KEY.pem] [--now ISO]
      lex derive --publisher lu-legilux --corpus PATH --out PATH [--code-version SHA]
    """);
