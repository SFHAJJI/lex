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
    private readonly object _gate = new();
    public string ModelId { get; }
    public string ModelRevision { get; }
    public int Dimensions { get; }
    public string ModelSha256 { get; }
    public string TokenizerSha256 { get; }

    private MultilingualE5Encoder(
        InferenceSession session, SentencePieceTokenizer tokenizer, PinnedEmbeddingModel manifest)
    {
        _session = session;
        _tokenizer = tokenizer;
        ModelId = manifest.ModelId;
        ModelRevision = manifest.Revision;
        Dimensions = manifest.Dimensions;
        ModelSha256 = manifest.Files["model.onnx"];
        TokenizerSha256 = manifest.Files["sentencepiece.bpe.model"];
    }

    public static MultilingualE5Encoder Open(string directory)
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

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Math.Min(2, Environment.ProcessorCount)),
        };
        var session = new InferenceSession(Path.Combine(directory, "model.onnx"), options);
        using var tokenizerStream = File.OpenRead(Path.Combine(directory, "sentencepiece.bpe.model"));
        try
        {
            var tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: true,
                addEndOfSentence: true, specialTokens: null);
            return new MultilingualE5Encoder(session, tokenizer, manifest);
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

    public float[] Encode(string text, EmbeddingInputKind kind)
    {
        var prefixed = (kind == EmbeddingInputKind.Query ? "query: " : "passage: ") + text;
        lock (_gate)
        {
            var tokenIds = _tokenizer.EncodeToIds(prefixed, addBeginningOfSentence: true, addEndOfSentence: true);
            if (tokenIds.Count > 512)
                tokenIds = tokenIds.Take(511).Append(_tokenizer.EndOfSentenceId).ToArray();
            var ids = tokenIds.Select(id => (long)id).ToArray();
            var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
            var shape = new[] { 1, ids.Length };
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
            if (tensor.Rank != 3 || tensor.Dimensions[2] != Dimensions)
                throw new InvalidDataException("Embedding model output shape is not [batch,tokens,384].");
            var result = new float[Dimensions];
            for (var token = 0; token < ids.Length; token++)
                for (var dimension = 0; dimension < Dimensions; dimension++)
                    result[dimension] += tensor[0, token, dimension];
            var norm = 0f;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] /= ids.Length;
                norm += result[i] * result[i];
            }
            norm = MathF.Sqrt(norm);
            if (norm == 0) throw new InvalidDataException("Embedding model returned a zero vector.");
            for (var i = 0; i < result.Length; i++) result[i] /= norm;
            return result;
        }
    }

    public void Dispose() => _session.Dispose();
}
