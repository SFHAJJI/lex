using System.Collections.ObjectModel;

namespace Lex.V3.Contracts.Source.Core;

public enum SourceObjectRefReaderOnlyInvariant
{
    CanonicalKeySha256ExactBytes = 1,
    CanonicalKeyUtf8Maximum4096Bytes = 2,
    ParentRegistryMatchesChild = 3,
    ParentIsNotSelf = 4,
}

/// <summary>
/// Cross-field and UTF-8-byte invariants enforced by <see cref="SourceObjectRef"/> that
/// Draft 2020-12 cannot express without extensions.
/// </summary>
public static class SourceObjectRefReaderOnlyInvariants
{
    public static IReadOnlyList<SourceObjectRefReaderOnlyInvariant> All { get; } =
        new ReadOnlyCollection<SourceObjectRefReaderOnlyInvariant>(
            Enum.GetValues<SourceObjectRefReaderOnlyInvariant>());
}
