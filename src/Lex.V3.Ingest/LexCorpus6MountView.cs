using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;

namespace Lex.V3.Ingest;

/// <summary>
/// The corpus a mount serves from, held to everything <see cref="VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify"/>
/// holds it to, without holding its members. The bytes are valid UTF-8; the document is one typed corpus manifest set
/// whose header validates; each member is read on its own, validates, sorts after the one before it, and is written
/// exactly as the canonical writer writes it, separated from the next by one comma and nothing else; and every byte outside
/// the members is the canonical writing of the set without them. That is the canonical check the whole-set parse makes,
/// one member at a time: the population's corpus (207 MB, 260,235 members) took 1.5 GB of objects and 3.4 GB at its peak
/// to verify whole, more than a small server holds (2026-10-13).
/// <para>
/// Only the members a mount looks up are kept: every EU member, and every Luxembourg member that holds a body or rights
/// evidence. A Luxembourg member recorded with neither (an unavailable item: 249,029 of the population) holds no article,
/// so no state names it and no answer looks it up.
/// </para>
/// </summary>
public sealed class LexCorpus6MountView
{
    private readonly Dictionary<(PublisherId Publisher, string ObjectRefSha256), LexCorpus6Member> _held;

    private LexCorpus6MountView(SourceArtifactRef artifactRef, LexCorpus6ManifestSet header, int memberCount, IReadOnlyList<LexCorpus6Member> held)
    {
        ArtifactRef = artifactRef;
        EuropeSourceSetRef = header.EuropeSourceSetRef;
        LuxembourgSourceSetRef = header.LuxembourgSourceSetRef;
        EuropeRightsMatrix = header.EuropeRightsMatrix;
        MemberCount = memberCount;
        HeldMembers = held;
        _held = held.ToDictionary(static member => (member.Publisher, member.ObjectRefSha256));
    }

    public SourceArtifactRef ArtifactRef { get; }

    public SourceArtifactRef EuropeSourceSetRef { get; }

    public SourceArtifactRef LuxembourgSourceSetRef { get; }

    public LexCorpus6EuropeRightsMatrix EuropeRightsMatrix { get; }

    /// <summary>How many members the corpus holds, every one of them verified.</summary>
    public int MemberCount { get; }

    /// <summary>The members kept, in the corpus's order.</summary>
    public IReadOnlyList<LexCorpus6Member> HeldMembers { get; }

    /// <summary>The kept member of a publisher by its object reference; null for one the corpus does not hold, or does not keep.</summary>
    public LexCorpus6Member? Member(PublisherId publisher, string objectRefSha256) =>
        _held.GetValueOrDefault((publisher, objectRefSha256));

    /// <summary>Whether a mount keeps the member: every EU member, and a Luxembourg member with a body or rights evidence.</summary>
    public static bool Holds(LexCorpus6Member member) =>
        member.Publisher != PublisherId.LuLegilux || member.BodySha256 is not null || member.LuxembourgRights is not null;

    public static LexCorpus6MountView ParseCanonicalAndVerify(byte[] canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(canonicalBytes);
        if (!Utf8.IsValid(canonicalBytes))
        {
            throw new ArgumentException("The corpus manifest-set bytes are not one valid typed document.", nameof(canonicalBytes));
        }

        var digest = LexCorpus6Builder.ComputeSha256(canonicalBytes);
        var artifactRef = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var members = new MemberStream(canonicalBytes);
        LexCorpus6ManifestSet header;
        try
        {
            header = ContractJson.DeserializeUtf8<LexCorpus6ManifestSet>(canonicalBytes, [members]);
            header.ValidateHeader();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException("The corpus manifest-set bytes are not one valid typed document.", nameof(canonicalBytes), exception);
        }

        if (members.Count == 0)
        {
            throw new ArgumentException("A corpus manifest set requires members.", nameof(canonicalBytes));
        }

        // Every byte outside the members' elements is the set's canonical writing without them.
        var without = LexCorpus6Builder.WriteWithoutMembers(header);
        var tail = "]}\n"u8;
        if (!without.AsSpan().EndsWith("[]}\n"u8) ||
            !canonicalBytes.AsSpan(0, (int)members.ArrayStart + 1).SequenceEqual(without.AsSpan(0, without.Length - tail.Length)) ||
            !canonicalBytes.AsSpan((int)members.ArrayEnd).SequenceEqual(tail))
        {
            throw new ArgumentException("The corpus manifest set is not its exact canonical representation.", nameof(canonicalBytes));
        }

        return new LexCorpus6MountView(artifactRef, header, members.Count, members.Held);
    }

    /// <summary>
    /// Reads the members array one member at a time: each validates, sorts after the previous one and is its own canonical
    /// writing, at the place the canonical writer puts it; the kept ones are the list the set is read with.
    /// </summary>
    private sealed class MemberStream(byte[] input) : JsonConverter<IReadOnlyList<LexCorpus6Member>>
    {
        public long ArrayStart { get; private set; } = -1;

        public long ArrayEnd { get; private set; } = -1;

        public int Count { get; private set; }

        public List<LexCorpus6Member> Held { get; } = [];

        public override IReadOnlyList<LexCorpus6Member> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (ArrayStart >= 0 || reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("The corpus members are not one array.");
            }

            ArrayStart = reader.TokenStartIndex;
            var next = ArrayStart + 1;
            LexCorpus6Member? previous = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    if (reader.TokenStartIndex != next)
                    {
                        throw new ArgumentException("The corpus manifest set is not its exact canonical representation.");
                    }

                    ArrayEnd = reader.TokenStartIndex;
                    return Held;
                }

                var start = reader.TokenStartIndex;
                if (start != next || reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new ArgumentException("The corpus manifest set is not its exact canonical representation.");
                }

                var member = JsonSerializer.Deserialize<LexCorpus6Member>(ref reader, options)
                    ?? throw new ArgumentException("A corpus manifest set requires members.");
                var end = reader.TokenStartIndex + 1;
                member.Validate();
                if (previous is not null && LexCorpus6ManifestSet.CompareMembers(previous, member) >= 0)
                {
                    throw new ArgumentException("Corpus members must be sorted and unique.");
                }

                if (!input.AsSpan((int)start, (int)(end - start)).SequenceEqual(LexCorpus6Builder.WriteMember(member)))
                {
                    throw new ArgumentException("The corpus manifest set is not its exact canonical representation.");
                }

                next = end < input.Length && input[end] == (byte)',' ? end + 1 : end;
                if (Holds(member))
                {
                    Held.Add(member);
                }

                previous = member;
                Count++;
            }

            throw new JsonException("The corpus members array does not end.");
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<LexCorpus6Member> value, JsonSerializerOptions options) =>
            throw new NotSupportedException("The member stream only reads.");
    }
}
