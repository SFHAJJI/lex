using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Quarantine;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

internal static class QuarantineFixtures
{
    public static PriorPublicCoordinate Coordinate(
        string workKey = "lu-legilux:eli/etat/leg/loi/2020-01-01/1",
        string language = "fr",
        string validFrom = "2020-01-01",
        string? anchor = "art_1er") =>
        new(workKey, language, validFrom, anchor);

    /// <summary>A small, distinct coordinate set, useful when a test needs more than one row.</summary>
    public static IReadOnlyList<PriorPublicCoordinate> CoordinateSet() =>
    [
        Coordinate(),
        Coordinate(workKey: "lu-legilux:eli/etat/leg/loi/2020-01-01/1", anchor: "art_2"),
        Coordinate(workKey: "eu-eurlex:celex:32020R0001", language: "en", anchor: null),
    ];

    public static QuarantineVerifierReceipt Receipt(string verifierIdentity = "quarantine-verifier-run-a") =>
        new(verifierIdentity, operatedReadOnly: true, producedAtUtc: "2026-09-03T10:00:00Z");

    public static SourceArtifactRef SourceIndexIdentity() =>
        new(
            "urn:uuid:11111111-1111-4111-8111-111111111111",
            new string('1', 64));

    public static string PriorIndexPairSha256() => new string('2', 64);

    public static QuarantineIssuer Issuer(string issuerId = "quarantine-reviewer-1", string keyId = "key-1") =>
        new(QuarantineIssuer.ExpectedRole, issuerId, keyId);

    public static QuarantineAttestation Attestation(string issuerId = "quarantine-reviewer-1") =>
        new(
            QuarantineAttestation.ExpectedPurpose,
            QuarantineAttestation.ExpectedAlgorithm,
            QuarantineAttestation.ExpectedSignatureFormat,
            new string('A', 86),
            Issuer(issuerId));
}
