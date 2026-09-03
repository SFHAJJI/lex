using System.Diagnostics.CodeAnalysis;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// An adversarial fixture for the shared construction-surface guard (C2).
///
/// <para>
/// <see cref="LeakyThing"/> presents itself as a closed type: private constructor, one checked
/// factory, exactly the shape four of this repository's real surface guards claim to protect. It is
/// riddled with additional construction paths. The guard's acceptance test is that it enumerates
/// every one of them in a fixture it did not write.
/// </para>
///
/// <para>
/// Why the fixture is not written by the guard's author. A fixture written beside its guard
/// contains only the doors its author thought of, so the guard sees all of them by construction and
/// the green result carries no information. That is not hypothetical here: on 2026-09-03 a guard in
/// this repository passed nineteen hand-written cases while twelve real spellings walked past it,
/// and four separate surface tests in this codebase were each wrong about a different reflection
/// scope. The doors below were chosen without sight of the guard.
/// </para>
///
/// <para>
/// Do not add a door to this file to make a guard pass, and do not remove one that a guard misses.
/// A missed door is the finding. Some doors are deliberately not enumerated in any comment.
/// </para>
/// </summary>
public static class SurfaceGuardLeakyFixture
{
    /// <summary>
    /// The type under guard. Its stated contract: only <see cref="Open"/> may produce one.
    /// </summary>
    public class LeakyThing : LeakyBase
    {
        private LeakyThing(string value)
            : base(value)
        {
        }

        /// <summary>The one path a reader is meant to believe in.</summary>
        public static LeakyThing? Open(string value, out string refusal)
        {
            refusal = string.Empty;
            if (string.IsNullOrEmpty(value))
            {
                refusal = "empty";
                return null;
            }

            return new LeakyThing(value);
        }

        /// <summary>Door: a second public static factory. A guard pinning one name misses it.</summary>
        public static LeakyThing Adopt(string value) => new(value);

        /// <summary>
        /// Door: a bool-returning method with an out parameter. A guard filtering on return type
        /// alone cannot see it, which is how a real guard in this repository was written.
        /// </summary>
        public static bool TryOpen(string value, [NotNullWhen(true)] out LeakyThing? thing)
        {
            thing = new LeakyThing(value);
            return true;
        }

        /// <summary>Door: a by-ref return.</summary>
        public static ref LeakyThing Borrow() => ref _slot;

        /// <summary>Door: a public static field holding one.</summary>
        public static LeakyThing Shared = new("shared");

        /// <summary>Door: a public readonly field. Readonly is not closed, only unassignable.</summary>
        public static readonly LeakyThing Constant = new("constant");

        /// <summary>Door: an instance method that produces a fresh one from an existing one.</summary>
        public LeakyThing Rebrand(string value) => new(value);

        /// <summary>
        /// Door: a generic wrapper. A guard comparing the return type to the guarded type sees
        /// <c>Task</c> and moves on.
        /// </summary>
        public static Task<LeakyThing> OpenAsync(string value) => Task.FromResult(new LeakyThing(value));

        /// <summary>Door: a tuple wrapper, for the same reason.</summary>
        public static (LeakyThing Thing, string Note) OpenWithNote(string value) =>
            (new LeakyThing(value), "note");

        /// <summary>Door: a conversion operator. Not a method a name-based guard looks for.</summary>
        public static implicit operator LeakyThing(string value) => new(value);

        /// <summary>Door: an internal factory, reachable from any friend assembly.</summary>
        internal static LeakyThing Friend(string value) => new(value);

        /// <summary>Door: a nested type whose whole purpose is to produce one.</summary>
        public static class Nested
        {
            public static LeakyThing Make(string value) => new(value);
        }

        private static LeakyThing _slot = new("slot");
    }

    /// <summary>
    /// Door: the base type's constructor is protected, so any type deriving from
    /// <see cref="LeakyThing"/>'s base can produce a value of the base's shape, and a guard that
    /// only inspects the guarded type never looks here.
    /// </summary>
    public abstract class LeakyBase
    {
        protected LeakyBase(string value) => Value = value;

        public string Value { get; }
    }

    /// <summary>
    /// Door: a producer declared on a completely different type. A guard that walks
    /// <c>typeof(LeakyThing).GetMethods()</c> cannot see this no matter which flags it uses; only
    /// an assembly sweep finds it.
    /// </summary>
    public static class Elsewhere
    {
        public static LeakyThing Produce(string value) => LeakyThing.Adopt(value);
    }

    /// <summary>
    /// A second guarded type, for the door a <c>record</c> emits whether its author wants it or
    /// not. Its author wrote one private constructor and believes that closes it.
    /// </summary>
    public sealed record LeakyRecord
    {
        private LeakyRecord(string value) => Value = value;

        public static LeakyRecord Open(string value) => new(value);

        public string Value { get; init; }
    }
}
