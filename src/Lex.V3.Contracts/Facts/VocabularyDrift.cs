using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The closed result produced when a publisher serves a term outside a closed vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Drift is a value, not an exception to be swallowed and not an <c>Unknown</c> member on the
/// enum itself. An <c>Unknown</c> member is the failure this type exists to prevent: it makes an
/// unrecognized term deserialize successfully, so the drift becomes invisible at exactly the
/// moment it should stop the pipeline.
/// </para>
/// <para>
/// Everything needed to act on the drift travels with it: which vocabulary, the raw term as
/// served, the closed set that was admitted at the time, and the observation that saw it. That
/// makes the report reproducible rather than a log line.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VocabularyDrift
{
    public const string Identity = FactsSchemaIds.VocabularyDrift;

    [JsonConstructor]
    public VocabularyDrift(
        string schema,
        VocabularyKind vocabulary,
        string observedTerm,
        IReadOnlyList<string> admittedTerms,
        SourceObservationReference observation)
    {
        if (!string.Equals(schema, Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The vocabulary drift schema must be version 1.", nameof(schema));
        }

        if (!FactsValidation.IsOpaqueIdentity(observedTerm))
        {
            throw new ArgumentException(
                "A drift must carry the term as the publisher served it.",
                nameof(observedTerm));
        }

        ArgumentNullException.ThrowIfNull(admittedTerms);
        var admitted = admittedTerms.ToArray();
        if (admitted.Length == 0 || Array.IndexOf(admitted, null) >= 0)
        {
            throw new ArgumentException(
                "A drift must record the nonempty closed set it was measured against.",
                nameof(admittedTerms));
        }

        if (Array.IndexOf(admitted, observedTerm) >= 0)
        {
            throw new ArgumentException(
                "A term inside the admitted set is not drift.",
                nameof(observedTerm));
        }

        Schema = schema;
        Vocabulary = vocabulary;
        ObservedTerm = observedTerm;
        AdmittedTerms = Array.AsReadOnly(admitted);
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    public string Schema { get; }

    public VocabularyKind Vocabulary { get; }

    public string ObservedTerm { get; }

    /// <summary>The closed set admitted when the drift was observed.</summary>
    public IReadOnlyList<string> AdmittedTerms { get; }

    public SourceObservationReference Observation { get; }
}

/// <summary>
/// Reads a publisher term against a closed vocabulary, returning either the value or a drift.
/// </summary>
/// <remarks>
/// There is no overload that returns a default on failure, and none that takes a fallback. The
/// only way to get a value out is to supply a term the closed set admits.
/// </remarks>
public static class ClosedVocabulary
{
    public static bool TryRead<TEnum>(
        string observedTerm,
        VocabularyKind vocabulary,
        SourceObservationReference observation,
        [NotNullWhen(true)] out TEnum? value,
        [NotNullWhen(false)] out VocabularyDrift? drift)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(observedTerm);
        ArgumentNullException.ThrowIfNull(observation);

        var admitted = WireNames<TEnum>();
        var index = Array.IndexOf(admitted, observedTerm);
        if (index >= 0)
        {
            value = Enum.GetValues<TEnum>()[index];
            drift = null;
            return true;
        }

        value = null;
        drift = new VocabularyDrift(
            FactsSchemaIds.VocabularyDrift,
            vocabulary,
            observedTerm,
            admitted,
            observation);
        return false;
    }

    /// <summary>
    /// The wire names of a closed enum, in declaration order.
    /// </summary>
    public static string[] WireNames<TEnum>()
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var names = new string[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var name = Enum.GetName(values[index])
                ?? throw new InvalidOperationException("A declared enum value has no name.");
            var field = typeof(TEnum).GetField(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("A declared enum field is missing.");
            names[index] = field
                .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), inherit: false)
                .OfType<JsonStringEnumMemberNameAttribute>()
                .FirstOrDefault()?.Name ?? name;
        }

        return names;
    }
}
