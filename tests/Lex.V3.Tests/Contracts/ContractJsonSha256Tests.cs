using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// <see cref="ContractJson.Sha256{T}"/> hashes the serializer's UTF-8 output while it is written, because a
/// whole-population document (the Luxembourg document phase's input, measured on the 2026-10-09 population replay) is
/// longer than any string the runtime can hold. Every digest it replaces was the SHA-256 of that string's UTF-8 bytes,
/// and has to stay exactly that, or a retained checkpoint would stop verifying.
/// </summary>
[TestClass]
public sealed class ContractJsonSha256Tests
{
    [TestMethod]
    public void TheStreamedDigestIsTheDigestOfTheSerializedStringsUtf8Bytes()
    {
        var reference = new SourceArtifactRef(
            "urn:uuid:7d0c4bb5-6b0a-4d2b-9f54-2f8c2b7f3c11", new string('a', 64));
        AssertSame(reference);
        AssertSame(new
        {
            Manifest = reference,
            Addresses = new[]
            {
                new
                {
                    Object = "http://data.legilux.public.lu/eli/etat/leg/code/travail/art._l._542-4_à_l._542-6./20201101",
                    Address = reference,
                },
            },
            Title = "Loi du 17 brumaire an V relative à la répartition des contributions — 📜",
            Count = 3,
            Empty = Array.Empty<string>(),
        });

        // Long enough that the serializer flushes to the stream many times before it completes.
        AssertSame(new { Rows = Enumerable.Range(0, 20_000).Select(static index => $"row {index} é").ToArray() });
    }

    private static void AssertSame<T>(T value) =>
        Assert.AreEqual(
            CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(value))),
            ContractJson.Sha256(value));
}
