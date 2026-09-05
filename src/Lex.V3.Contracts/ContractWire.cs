using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

/// <summary>
/// The exact wire token of a closed vocabulary member.
/// </summary>
/// <remarks>
/// Canonical projections are built from these tokens rather than from CLR member names, so the
/// text a digest covers is the text the wire carries. Reading the attribute rather than restating
/// the tokens in a switch means a member added without a token fails loudly at its first use
/// instead of silently projecting its CLR spelling.
/// </remarks>
public static class ContractWire
{
    public static string NameOf<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        ContractValidation.RequireDefined(value, nameof(value));
        var name = Enum.GetName(value)
            ?? throw new ArgumentOutOfRangeException(nameof(value));
        var field = typeof(TEnum).GetField(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new ArgumentOutOfRangeException(nameof(value));
        var token = field
            .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), inherit: false)
            .OfType<JsonStringEnumMemberNameAttribute>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{typeof(TEnum).Name}.{name} carries no wire token.");
        return token.Name;
    }
}
