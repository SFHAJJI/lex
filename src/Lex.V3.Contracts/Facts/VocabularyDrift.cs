using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
/// <see cref="AdmittedTerms"/> must be **exactly** the wire names of the vocabulary
/// <see cref="Vocabulary"/> names, in declaration order. Candidate 1 accepted any list at all,
/// so a report could read a term through one vocabulary and label it with another: Codex read a
/// date role while the report claimed the vocabulary was relation predicates, and nothing
/// objected. A report whose label does not match the set it was measured against sends the
/// reader to the wrong contract, which is worse than no report.
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

        FactsValidation.RequireDefined(vocabulary, nameof(vocabulary));

        if (!FactsValidation.IsOpaqueIdentity(observedTerm))
        {
            throw new ArgumentException(
                "A drift must carry the term as the publisher served it.",
                nameof(observedTerm));
        }

        ArgumentNullException.ThrowIfNull(admittedTerms);
        var admitted = admittedTerms.ToArray();
        if (admitted.Distinct(StringComparer.Ordinal).Count() != admitted.Length)
        {
            throw new ArgumentException(
                "The admitted set cannot repeat a term.",
                nameof(admittedTerms));
        }

        var expected = ClosedVocabulary.AdmittedTermsFor(vocabulary);
        if (!admitted.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"The admitted set must be exactly the {vocabulary} vocabulary, in declaration order.",
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

    /// <summary>The closed set the term was measured against.</summary>
    public IReadOnlyList<string> AdmittedTerms { get; }

    public SourceObservationReference Observation { get; }
}

/// <summary>
/// Reads a publisher term against a closed vocabulary, returning either the value or a drift.
/// </summary>
/// <remarks>
/// There is no overload that returns a default on failure, none that takes a fallback, and
/// **none that lets the caller choose the vocabulary label**. The kind is derived from the enum
/// through <see cref="FactsVocabularies.KindFor{TEnum}"/>, so a report can only ever name the
/// vocabulary it actually consulted.
/// </remarks>
public static class ClosedVocabulary
{
    public static bool TryRead<TEnum>(
        string observedTerm,
        SourceObservationReference observation,
        [NotNullWhen(true)] out TEnum? value,
        [NotNullWhen(false)] out VocabularyDrift? drift)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(observedTerm);
        ArgumentNullException.ThrowIfNull(observation);

        var kind = FactsVocabularies.KindFor<TEnum>();
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
            kind,
            observedTerm,
            admitted,
            observation);
        return false;
    }

    /// <summary>The wire names of the vocabulary a kind names, in declaration order.</summary>
    public static string[] AdmittedTermsFor(VocabularyKind kind) => kind switch
    {
        VocabularyKind.RelationAssertionKind => WireNames<RelationAssertionKind>(),
        VocabularyKind.IdentifierFamily => WireNames<FactsIdentifierFamily>(),
        VocabularyKind.EcliState => WireNames<EcliState>(),
        VocabularyKind.TargetBodyScope => WireNames<TargetBodyScope>(),
        VocabularyKind.DateSemanticRole => WireNames<DateSemanticRole>(),
        VocabularyKind.DatePrecision => WireNames<DatePrecision>(),
        VocabularyKind.DateOpenSentinel => WireNames<DateOpenSentinel>(),
        _ => throw new ArgumentException("Unknown vocabulary kind.", nameof(kind)),
    };

    /// <summary>The wire names of a closed enum, in declaration order.</summary>
    public static string[] WireNames<TEnum>()
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var names = new string[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var name = Enum.GetName(values[index])
                ?? throw new InvalidOperationException("A declared enum value has no name.");
            var field = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("A declared enum field is missing.");
            names[index] = field
                .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name;
        }

        return names;
    }
}
