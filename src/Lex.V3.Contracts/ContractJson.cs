using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Lex.V3.Contracts;

public static class ContractJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions(exactEnums: true);

    public static string Serialize<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var runtimeType = value.GetType();
        var contractType = FindRegisteredPolymorphicContract(runtimeType) ?? typeof(T);
        return JsonSerializer.Serialize(value, contractType, Options);
    }

    public static T Deserialize<T>(string json)
    {
        try
        {
            var requestedType = typeof(T);
            var contractType = FindRegisteredPolymorphicContract(requestedType) ?? requestedType;
            var value = JsonSerializer.Deserialize(json, contractType, Options)
                ?? throw new JsonException("The contract document cannot be null.");
            return value is T typed
                ? typed
                : throw new JsonException(
                    $"The contract discriminator does not name {requestedType.Name}.");
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("The contract document violates a typed invariant.", exception);
        }
    }

    public static JsonSerializerOptions CreateSchemaOptions() => CreateOptions(exactEnums: false);

    private static Type? FindRegisteredPolymorphicContract(Type type)
    {
        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.GetCustomAttribute<JsonPolymorphicAttribute>() is null)
            {
                continue;
            }

            if (candidate == type || candidate
                .GetCustomAttributes<JsonDerivedTypeAttribute>()
                .Any(attribute => attribute.DerivedType == type))
            {
                return candidate;
            }
        }

        return null;
    }

    private static JsonSerializerOptions CreateOptions(bool exactEnums)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            AllowTrailingCommas = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };

        if (exactEnums)
        {
            options.Converters.Add(new ValidUnicodeStringConverter());
        }

        options.Converters.Add(exactEnums
            ? new ExactStringEnumConverterFactory()
            : new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }
}

internal sealed class ValidUnicodeStringConverter : JsonConverter<string>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => reader.GetString();

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        EnsureValidUnicode(value);
        writer.WriteStringValue(value);
    }

    public override string ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => reader.GetString()!;

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        EnsureValidUnicode(value);
        writer.WritePropertyName(options.DictionaryKeyPolicy?.ConvertName(value) ?? value);
    }

    private static void EnsureValidUnicode(string value)
    {
        for (var index = 0; index < value.Length;)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new JsonException("Contract strings must contain valid Unicode scalars.");
            }

            index += consumed;
        }
    }
}

internal sealed class ExactStringEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)(Activator.CreateInstance(
            typeof(ExactStringEnumConverter<>).MakeGenericType(typeToConvert))
            ?? throw new InvalidOperationException("Could not create an exact enum converter."));
}

internal sealed class ExactStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> ValuesByWireName;
    private static readonly IReadOnlyDictionary<TEnum, string> WireNamesByValue;

    static ExactStringEnumConverter()
    {
        var valuesByWireName = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        var wireNamesByValue = new Dictionary<TEnum, string>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var name = Enum.GetName(value)
                ?? throw new InvalidOperationException("A declared enum value has no name.");
            var field = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("A declared enum field is missing.");
            var wireName = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name;
            if (!valuesByWireName.TryAdd(wireName, value) || !wireNamesByValue.TryAdd(value, wireName))
            {
                throw new InvalidOperationException("Enum wire names and values must be unique.");
            }
        }

        ValuesByWireName = valuesByWireName;
        WireNamesByValue = wireNamesByValue;
    }

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !ValuesByWireName.TryGetValue(reader.GetString()!, out var value))
        {
            throw new JsonException($"Unknown {typeof(TEnum).Name} wire value.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!WireNamesByValue.TryGetValue(value, out var wireName))
        {
            throw new JsonException($"Undefined {typeof(TEnum).Name} value.");
        }

        writer.WriteStringValue(wireName);
    }
}
