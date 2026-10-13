using System.Text;
using Lex.V3.Api;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The corpus as a mount keeps it (<see cref="LexCorpus6MountView"/>) is held to what the whole-set parse holds it to,
/// one member at a time: the same bytes verify, and every alteration the whole-set parse refuses, it refuses.
/// </summary>
[TestClass]
public sealed class LexCorpus6MountViewTests
{
    private static async Task<byte[]> FixtureCorpusAsync()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        return await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName));
    }

    [TestMethod]
    public async Task TheViewVerifiesTheCorpusTheWholeSetVerifiesAndKeepsTheMembersAMountLooksUp()
    {
        var bytes = await FixtureCorpusAsync();
        var whole = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(bytes);
        var view = LexCorpus6MountView.ParseCanonicalAndVerify(bytes);

        Assert.AreEqual(whole.ArtifactRef, view.ArtifactRef);
        Assert.AreEqual(whole.Set.EuropeSourceSetRef, view.EuropeSourceSetRef);
        Assert.AreEqual(whole.Set.LuxembourgSourceSetRef, view.LuxembourgSourceSetRef);
        Assert.AreEqual(whole.Set.EuropeRightsMatrix.CapturedAt, view.EuropeRightsMatrix.CapturedAt);
        Assert.AreEqual(whole.Set.Members.Count, view.MemberCount);
        CollectionAssert.AreEqual(
            whole.Set.Members.Where(LexCorpus6MountView.Holds).Select(static member => (member.Publisher, member.ObjectRefSha256)).ToArray(),
            view.HeldMembers.Select(static member => (member.Publisher, member.ObjectRefSha256)).ToArray());
        Assert.IsNotEmpty(view.HeldMembers, "the fixture holds members with bodies");
        foreach (var member in whole.Set.Members)
        {
            var held = view.Member(member.Publisher, member.ObjectRefSha256);
            if (LexCorpus6MountView.Holds(member))
            {
                Assert.AreEqual(member.BodySha256, held!.BodySha256);
                Assert.AreEqual(member.LuxembourgRights?.SelectedWemi.ManifestationIri, held.LuxembourgRights?.SelectedWemi.ManifestationIri);
            }
            else
            {
                Assert.IsNull(held, "a Luxembourg member with neither body nor rights is not kept");
            }
        }

        // Each member's bytes are exactly what the canonical writer writes for it, so the check made one member at a time
        // is the whole-set one: the set's canonical writing is its header, its members joined by commas, and its tail.
        var without = LexCorpus6Builder.WriteWithoutMembers(whole.Set);
        var joined = string.Join(",", whole.Set.Members.Select(static member => Encoding.UTF8.GetString(LexCorpus6Builder.WriteMember(member))));
        var rebuilt = Encoding.UTF8.GetString(without, 0, without.Length - 3) + joined + "]}\n";
        Assert.AreEqual(Encoding.UTF8.GetString(bytes), rebuilt);
    }

    [TestMethod]
    public async Task EveryAlterationTheWholeSetRefusesTheViewRefuses()
    {
        var bytes = await FixtureCorpusAsync();
        var text = Encoding.UTF8.GetString(bytes);
        StringAssert.Contains(text, "},{\"publisher\":", "the fixture corpus holds more than one member");
        var membersAt = text.IndexOf("\"members\":[", StringComparison.Ordinal) + "\"members\":[".Length;
        var firstMemberEnd = text.IndexOf("},{\"publisher\":", membersAt, StringComparison.Ordinal) + 1;
        var firstMember = text[membersAt..firstMemberEnd];
        var outcome = text.IndexOf("\"outcome\":\"", membersAt, StringComparison.Ordinal) + "\"outcome\":\"".Length;

        var alterations = new (string Name, byte[] Bytes)[]
        {
            ("a space between two members", Bytes(text.Insert(firstMemberEnd + 1, " "))),
            ("a space after the members array opens", Bytes(text.Insert(membersAt, " "))),
            ("a newline before the members array closes", Bytes(text.Insert(text.Length - 3, "\n"))),
            ("a member twice", Bytes(text.Insert(firstMemberEnd, "," + firstMember))),
            ("a member's value escaped", Bytes(text[..outcome] + "\\u00" + Convert.ToHexStringLower([(byte)text[outcome]]) + text[(outcome + 1)..])),
            ("a property a member does not have", Bytes(text.Insert(firstMemberEnd - 1, ",\"extra\":1"))),
            ("a space before the document", Bytes(" " + text)),
            ("a second newline after it", Bytes(text + "\n")),
            ("a header value changed", Bytes(text.Replace("\"schema\":\"", "\"schema\":\"x", StringComparison.Ordinal))),
            ("no members", Bytes(text[..membersAt] + text[(text.Length - 3)..])),
            ("bytes that are not UTF-8", Invalid(bytes, Encoding.UTF8.GetByteCount(text[..(outcome + 1)]))),
        };

        foreach (var (name, altered) in alterations)
        {
            Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(altered), $"the whole set refuses {name}");
            Assert.ThrowsExactly<ArgumentException>(() => LexCorpus6MountView.ParseCanonicalAndVerify(altered), $"the view refuses {name}");
        }

        static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

        static byte[] Invalid(byte[] source, int at)
        {
            var copy = source.ToArray();
            copy[at] = 0xFF;
            return copy;
        }
    }

    [TestMethod]
    public async Task AMountKeepsTheViewAndAnswersAsBefore()
    {
        // The mount opens with the view, and its answers name the corpus by the digest the whole set would.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = await EnvelopeAsync(mount, "/api/v3/coverage", "coverage", new { });
        Assert.AreEqual(
            VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(bytes).ArtifactRef.Sha256,
            envelope.Result!.Value.GetProperty("mounted").GetProperty("corpus_sha256").GetString());
    }
}
