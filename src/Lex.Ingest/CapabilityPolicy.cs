using System.Security.Cryptography;
using System.Text.Json;
using Lex.Index;

namespace Lex.Ingest;

internal static class CapabilityPolicy
{
    private const int MaximumPolicyBytes = 64 * 1024;

    internal static CapabilityBuildExpectation Load(string path, string collection)
    {
        var bytes = ReadBounded(path);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            RequireObject(root, "policy");
            RequireProperties(root, "policy", "schema", "collections");
            var schema = root.GetProperty("schema");
            if (schema.ValueKind != JsonValueKind.String
                || schema.GetString() != "lex-capability-policy/1")
                throw new InvalidDataException("Unsupported capability policy schema.");
            var collections = root.GetProperty("collections");
            RequireObject(collections, "collections");
            if (!collections.EnumerateObject().Any())
                throw new InvalidDataException("Capability policy must name a collection.");

            var parsed = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var item in collections.EnumerateObject())
            {
                if (item.Name.Length is < 1 or > 128)
                    throw new InvalidDataException("Capability policy collection id is invalid.");
                RequireObject(item.Value, $"collection {item.Name}");
                RequireProperties(item.Value, $"collection {item.Name}", "unsupported_filters");
                var unsupported = item.Value.GetProperty("unsupported_filters");
                if (unsupported.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException(
                        $"Collection {item.Name} unsupported_filters must be an array.");
                var filters = unsupported.EnumerateArray().Select(value =>
                    value.ValueKind == JsonValueKind.String
                        ? value.GetString()!
                        : throw new InvalidDataException(
                            $"Collection {item.Name} has a non-string unsupported filter."))
                    .ToArray();
                if (filters.Length > CapabilityManifest.GovernedFilters.Count
                    || filters.Distinct(StringComparer.Ordinal).Count() != filters.Length
                    || !filters.SequenceEqual(filters.Order(StringComparer.Ordinal),
                        StringComparer.Ordinal))
                    throw new InvalidDataException(
                        $"Collection {item.Name} unsupported filters must be unique and ordinal-sorted.");
                try
                {
                    _ = CapabilityBuildExpectation.Production(
                        item.Name, filters, new string('0', 64));
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException(
                        $"Collection {item.Name} capability policy is invalid.", error);
                }
                if (!parsed.TryAdd(item.Name, filters))
                    throw new InvalidDataException(
                        $"Capability policy repeats collection '{item.Name}'.");
            }
            if (!parsed.TryGetValue(collection, out var expectedUnsupported))
                throw new InvalidDataException(
                    $"Capability policy does not name collection '{collection}'.");
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return CapabilityBuildExpectation.Production(
                collection, expectedUnsupported, digest);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Capability policy is not strict JSON.", error);
        }
    }

    private static byte[] ReadBounded(string path)
    {
        try
        {
            using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.SequentialScan,
            });
            if (stream.Length is < 1 or > MaximumPolicyBytes)
                throw new InvalidDataException(
                    $"Capability policy must be between 1 and {MaximumPolicyBytes} bytes.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                throw new InvalidDataException(
                    $"Capability policy must be at most {MaximumPolicyBytes} bytes.");
            return bytes;
        }
        catch (Exception error) when ((error is IOException
                                       || error is UnauthorizedAccessException)
                                      && error is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"Capability policy cannot be read: {path}", error);
        }
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Capability {label} must be an object.");
    }

    private static void RequireProperties(
        JsonElement value, string label, params string[] expected)
    {
        var actual = value.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        var required = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(required, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"Capability {label} properties must be exactly: {string.Join(", ", required)}.");
    }
}
