using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Lex.Index;

public sealed record PinnedEmbeddingModel(
    string Schema,
    string ModelId,
    string Revision,
    int Dimensions,
    IReadOnlyDictionary<string, string> Files);

/// <summary>Local, deterministic multilingual-e5-small inference. No network path exists here.</summary>
public sealed class MultilingualE5Encoder : ITextEncoder
{
    private readonly InferenceSession _session;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly int _paddingId;
    private readonly object _gate = new();
    public string ModelId { get; }
    public string ModelRevision { get; }
    public int Dimensions { get; }
    public string ModelSha256 { get; }
    public string TokenizerSha256 { get; }
    public string ExecutionProvider { get; }

    private MultilingualE5Encoder(
        InferenceSession session, SentencePieceTokenizer tokenizer, PinnedEmbeddingModel manifest,
        string executionProvider)
    {
        _session = session;
        _tokenizer = tokenizer;
        ModelId = manifest.ModelId;
        ModelRevision = manifest.Revision;
        Dimensions = manifest.Dimensions;
        ModelSha256 = manifest.Files["model.onnx"];
        TokenizerSha256 = manifest.Files["sentencepiece.bpe.model"];
        ExecutionProvider = executionProvider;
        // This raw SentencePiece model has no Hugging Face-added <pad> entry. Padding positions
        // are excluded by both the attention mask and mean pooling, so any valid vocabulary id
        // is only a tensor placeholder; use the model's unknown id when no explicit pad exists.
        if (!tokenizer.Vocabulary.TryGetValue("<pad>", out _paddingId))
            _paddingId = tokenizer.UnknownId;
    }

    public static MultilingualE5Encoder Open(
        string directory, int? intraOpThreads = null, int? directMlDeviceId = null)
    {
        var manifestPath = Path.Combine(directory, "model-manifest.json");
        var manifest = JsonSerializer.Deserialize<PinnedEmbeddingModel>(
            File.ReadAllBytes(manifestPath), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? throw new InvalidDataException("Embedding model manifest is empty.");
        if (manifest.Schema != "lex-embedding-model/1" || manifest.Dimensions != 384
            || manifest.ModelId != "intfloat/multilingual-e5-small")
            throw new InvalidDataException("Embedding model manifest is not the pinned multilingual-e5-small contract.");

        foreach (var (relative, expected) in manifest.Files)
        {
            var path = Path.Combine(directory, relative);
            using var artifact = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(artifact));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Embedding artifact '{relative}' failed its SHA-256 check.");
        }

        if (intraOpThreads <= 0)
            throw new ArgumentOutOfRangeException(nameof(intraOpThreads));
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (directMlDeviceId is { } deviceId)
        {
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.EnableMemoryPattern = false;
            options.AppendExecutionProvider_DML(deviceId);
        }
        else
        {
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = intraOpThreads
                ?? Math.Max(1, Math.Min(2, Environment.ProcessorCount));
        }
        var session = new InferenceSession(Path.Combine(directory, "model.onnx"), options);
        using var tokenizerStream = File.OpenRead(Path.Combine(directory, "sentencepiece.bpe.model"));
        try
        {
            var tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: true,
                addEndOfSentence: true, specialTokens: null);
            var runtime = typeof(InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown";
            var provider = directMlDeviceId is { } id
                ? $"directml:{id};onnxruntime={runtime}"
                : $"cpu;onnxruntime={runtime};intra_op_threads={options.IntraOpNumThreads}";
            return new MultilingualE5Encoder(session, tokenizer, manifest, provider);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public int CountTokens(string text) => _tokenizer.CountTokens(text);

    public int PrefixLengthForTokens(string text, int maxTokens) =>
        _tokenizer.GetIndexByTokenCount(text, maxTokens, out _, out _);

    public int SuffixStartForTokens(string text, int maxTokens) =>
        _tokenizer.GetIndexByTokenCountFromEnd(text, maxTokens, out _, out _);

    public float[] Encode(string text, EmbeddingInputKind kind) => EncodeBatch([text], kind)[0];

    public IReadOnlyList<float[]> EncodeBatch(IReadOnlyList<string> texts, EmbeddingInputKind kind)
    {
        if (texts.Count == 0) return [];
        var prefix = kind == EmbeddingInputKind.Query ? "query: " : "passage: ";
        lock (_gate)
        {
            var tokenized = new long[texts.Count][];
            var maxTokens = 0;
            for (var batch = 0; batch < texts.Count; batch++)
            {
                var tokenIds = _tokenizer.EncodeToIds(prefix + texts[batch],
                    addBeginningOfSentence: true, addEndOfSentence: true);
                if (tokenIds.Count > 512)
                    tokenIds = tokenIds.Take(511).Append(_tokenizer.EndOfSentenceId).ToArray();
                tokenized[batch] = tokenIds.Select(id => (long)id).ToArray();
                maxTokens = Math.Max(maxTokens, tokenized[batch].Length);
            }

            var ids = Enumerable.Repeat((long)_paddingId, texts.Count * maxTokens).ToArray();
            var mask = new long[ids.Length];
            for (var batch = 0; batch < tokenized.Length; batch++)
            {
                tokenized[batch].CopyTo(ids, batch * maxTokens);
                Array.Fill(mask, 1L, batch * maxTokens, tokenized[batch].Length);
            }
            var shape = new[] { texts.Count, maxTokens };
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, shape)),
            };
            if (_session.InputMetadata.ContainsKey("token_type_ids"))
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(new long[ids.Length], shape)));

            using var output = _session.Run(inputs);
            var tensor = output.First(x => x.Name is "last_hidden_state" or "token_embeddings").AsTensor<float>();
            if (tensor.Rank != 3 || tensor.Dimensions[0] != texts.Count
                || tensor.Dimensions[1] != maxTokens || tensor.Dimensions[2] != Dimensions)
                throw new InvalidDataException("Embedding model output shape is not [batch,tokens,384].");
            var results = new float[texts.Count][];
            for (var batch = 0; batch < texts.Count; batch++)
            {
                var result = new float[Dimensions];
                var tokenCount = tokenized[batch].Length;
                for (var token = 0; token < tokenCount; token++)
                    for (var dimension = 0; dimension < Dimensions; dimension++)
                        result[dimension] += tensor[batch, token, dimension];
                var norm = 0f;
                for (var dimension = 0; dimension < result.Length; dimension++)
                {
                    result[dimension] /= tokenCount;
                    norm += result[dimension] * result[dimension];
                }
                norm = MathF.Sqrt(norm);
                if (norm == 0) throw new InvalidDataException("Embedding model returned a zero vector.");
                for (var dimension = 0; dimension < result.Length; dimension++) result[dimension] /= norm;
                results[batch] = result;
            }
            return results;
        }
    }

    public void Dispose() => _session.Dispose();
}
