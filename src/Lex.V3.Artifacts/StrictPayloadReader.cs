using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

internal static class StrictPayloadReader
{
    public static bool IsStructurallyValid(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(
                bytes,
                new JsonReaderOptions
                {
                    AllowMultipleValues = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = PreviewContractLimits.MaximumPayloadDepth,
                });
            var frames = new Stack<ContainerFrame>();
            var tokenCount = 0;
            while (reader.Read())
            {
                if (++tokenCount > PreviewContractLimits.MaximumPayloadTokens)
                {
                    return false;
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        CountArrayItem(frames);
                        frames.Push(ContainerFrame.Object());
                        break;
                    case JsonTokenType.EndObject:
                        if (frames.Count == 0 || !frames.Pop().IsObject)
                        {
                            return false;
                        }

                        break;
                    case JsonTokenType.StartArray:
                        CountArrayItem(frames);
                        frames.Push(ContainerFrame.Array());
                        break;
                    case JsonTokenType.EndArray:
                        if (frames.Count == 0 || frames.Pop().IsObject)
                        {
                            return false;
                        }

                        break;
                    case JsonTokenType.PropertyName:
                        {
                            if (frames.Count == 0 || !frames.Peek().IsObject)
                            {
                                return false;
                            }

                            var frame = frames.Peek();
                            if (++frame.Count > PreviewContractLimits.MaximumObjectMembers)
                            {
                                return false;
                            }

                            var name = reader.GetString()!;
                            if (Encoding.UTF8.GetByteCount(name) > PreviewContractLimits.MaximumPayloadPropertyNameBytes ||
                                !frame.PropertyNames!.Add(name))
                            {
                                return false;
                            }

                            break;
                        }
                    case JsonTokenType.String:
                        CountArrayItem(frames);
                        if (Encoding.UTF8.GetByteCount(reader.GetString()!) >
                            PreviewContractLimits.MaximumPayloadStringBytes)
                        {
                            return false;
                        }

                        break;
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        CountArrayItem(frames);
                        break;
                }
            }

            return frames.Count == 0 && tokenCount > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void CountArrayItem(Stack<ContainerFrame> frames)
    {
        if (frames.Count == 0 || frames.Peek().IsObject)
        {
            return;
        }

        var frame = frames.Peek();
        if (++frame.Count > PreviewContractLimits.MaximumArrayItems)
        {
            throw new JsonException("The preview array item limit was exceeded.");
        }
    }

    private sealed class ContainerFrame
    {
        private ContainerFrame(bool isObject)
        {
            IsObject = isObject;
            PropertyNames = isObject ? new HashSet<string>(StringComparer.Ordinal) : null;
        }

        public bool IsObject { get; }

        public int Count { get; set; }

        public HashSet<string>? PropertyNames { get; }

        public static ContainerFrame Object() => new(true);

        public static ContainerFrame Array() => new(false);
    }
}
