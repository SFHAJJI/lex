using System.Reflection;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Custody.Azure;

namespace Lex.V3.Custody.Probe;

/// <summary>Retains one content-pinned accepted population for a later fresh-process replay.</summary>
internal static class FactPopulationCommissioning
{
    internal const string ProposalSha256 =
        "2e134d982964af2592aac5002e0b2672b775e87288a80faa054ece0c4388f835";
    private const string SourceRouteSha256 =
        "717145e555f51dbb5611275e3431ccdf07c3e084321dbbf70d8f0b25c9d79bac";
    private const string SourceBodySha256 =
        "18841181f2cdb0ec6ae55e666286ae57f4561cb81b08235c8b35dcf8aa9d5411";
    private const string SourceObservationId =
        "urn:uuid:f791f5f7-bb2f-4065-a711-0df7490cc099";
    private const string SourceRequestUri =
        "https://publications.europa.eu/webapi/rdf/sparql";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] FactSha256s =
    [
        "bf9559bb52f0048a09b99f9e9d2c1eadba3aa5d0d54f58027cf8ca13429443a3",
        "fe697aefe109e7549c5229a36becb5faf7d05534f7979f3f5b5aafdfa2d2e210",
        "1226f821ad7a7e5c7eb44543f2921fa4637421f503e2dbaffcc8e62d47cca5dc",
        "29a41dedf9c0f608b808a271fe130780ca18dc9f8ae9f88bf91b8d919ac1fe7e",
        "fce1b84a8c70811fd1f37c4799321280e03caf33153785451ac39d660b183bfb",
    ];

    internal static async Task<string> RunAsync(
        ICustodyStore store,
        AzureBlobCustodyOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        var assets = LoadAndValidateAssets();

        var bodyReceipt = await HoldExactAsync(assets.SourceBody, store, options, cancellationToken)
            .ConfigureAwait(false);
        var bodyReceiptBytes = DurableBlobWriteReceiptDigest.CanonicalBytes(bodyReceipt);
        var bodyReceiptSha256 = DurableBlobWriteReceiptDigest.Of(bodyReceipt);
        _ = await HoldExactAsync(bodyReceiptBytes, store, options, cancellationToken)
            .ConfigureAwait(false);

        var oldHop = assets.SourceRoute.Hops.Single();
        var commissionedHop = RoutedHttpHop.Create(
            oldHop.Ordinal,
            oldHop.ObservationId,
            oldHop.AntecedentHopObservationId,
            oldHop.LogicalRequestSha256,
            oldHop.RequestUri,
            oldHop.Status,
            oldHop.Headers,
            oldHop.RequestStartedAt,
            oldHop.TerminalObservedAt,
            oldHop.Completion,
            oldHop.Length,
            oldHop.Sha256,
            bodyReceiptSha256,
            oldHop.ReadbackByteLength,
            oldHop.ReadbackSha256);
        var commissionedRoute = RoutedHttpEvidence.Create(
            assets.SourceRoute.RunIdentity,
            assets.SourceRoute.RequestOrdinal,
            assets.SourceRoute.AttemptOrdinal,
            [commissionedHop],
            assets.SourceRoute.Outcome,
            new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)
            {
                [SourceObservationId] = bodyReceipt,
            });
        var commissionedRouteBytes = commissionedRoute.CopyCanonicalBytes();
        var commissionedRouteSha256 = CustodyDigest.Of(commissionedRouteBytes);
        _ = await HoldExactAsync(commissionedRouteBytes, store, options, cancellationToken)
            .ConfigureAwait(false);

        foreach (var fact in assets.Facts)
        {
            _ = await HoldExactAsync(fact, store, options, cancellationToken).ConfigureAwait(false);
        }

        var replayInputBytes = StrictUtf8.GetBytes(ContractJson.Serialize(new
        {
            Schema = "lex-v3-custody-replay-input/1",
            Facts = FactSha256s,
            Routes = new[] { commissionedRouteSha256 },
        }));
        var replayInputSha256 = CustodyDigest.Of(replayInputBytes);
        _ = await HoldExactAsync(replayInputBytes, store, options, cancellationToken)
            .ConfigureAwait(false);

        return ContractJson.Serialize(new
        {
            Schema = "lex-v3-custody-fact-population-commissioning/1",
            ProposalSha256,
            SourceBodySha256,
            SourceRouteSha256,
            SourceObservationId,
            BodyWriteReceiptSha256 = bodyReceiptSha256,
            CommissionedRouteSha256 = commissionedRouteSha256,
            FactSha256s,
            ReplayInputSha256 = replayInputSha256,
        });
    }

    private static PopulationAssets LoadAndValidateAssets()
    {
        var proposal = ReadResource("proposal.bin");
        var sourceRouteBytes = ReadResource("source-route.bin");
        var sourceBody = ReadResource("source-body.bin");
        var facts = FactSha256s.Select(digest => ReadResource($"facts.{digest}.bin")).ToArray();

        RequireDigest(proposal, ProposalSha256, "proposal");
        RequireDigest(sourceRouteBytes, SourceRouteSha256, "source route");
        RequireDigest(sourceBody, SourceBodySha256, "source body");
        if (sourceBody.LongLength != 54_084)
            throw new CustodyIntegrityException("The commissioned source body length changed.");

        RoutedHttpEvidence route;
        try { route = RoutedHttpEvidence.ParseAndVerify(sourceRouteBytes); }
        catch (ArgumentException exception)
        {
            throw new CustodyIntegrityException("The commissioned source route is not canonical.", exception);
        }
        if (route.Hops.Count != 1
            || route.Outcome is not CompleteHttpRouteOutcome
            || route.Hops[0].StatusDisposition != HttpStatusDisposition.DerivableStatus
            || route.Hops[0].Completion is IncompleteHttpCompletion
            || route.Hops[0].Status != 200
            || route.Hops[0].ObservationId != SourceObservationId
            || route.Hops[0].RequestUri != SourceRequestUri
            || route.Hops[0].Sha256 != SourceBodySha256
            || route.Hops[0].Length != checked((ulong)sourceBody.LongLength))
        {
            throw new CustodyIntegrityException(
                "The commissioned source route is not the pinned complete derivable observation.");
        }

        for (var index = 0; index < facts.Length; index++)
        {
            RequireDigest(facts[index], FactSha256s[index], "Fact");
            PublisherRelation fact;
            try
            {
                var json = StrictUtf8.GetString(facts[index]);
                fact = ContractJson.Deserialize<PublisherRelation>(json);
                if (!string.Equals(ContractJson.Serialize(fact), json, StringComparison.Ordinal))
                    throw new CustodyIntegrityException("A commissioned Fact is not canonical.");
            }
            catch (Exception exception) when (exception is not CustodyIntegrityException)
            {
                throw new CustodyIntegrityException("A commissioned Fact violates its typed contract.", exception);
            }
            if (fact.SourceObservationId != SourceObservationId)
                throw new CustodyIntegrityException("A commissioned Fact is outside the pinned source population.");
        }

        return new PopulationAssets(sourceBody, route, facts);
    }

    private static async Task<DurableBlobWriteReceipt> HoldExactAsync(
        byte[] bytes,
        ICustodyStore store,
        AzureBlobCustodyOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is null
            || receipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d
            || receipt.Reference.ByteLength != bytes.LongLength
            || receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes))
        {
            throw new CustodyIntegrityException("The custody store returned a receipt for different commissioning bytes.");
        }
        CustodyProbeApplication.ValidateConfiguredReceipt(receipt, options, bytes.LongLength);
        return receipt;
    }

    private static void RequireDigest(byte[] bytes, string expected, string name)
    {
        if (CustodyDigest.Of(bytes) != expected)
            throw new CustodyIntegrityException($"The commissioned {name} digest changed.");
    }

    private static byte[] ReadResource(string suffix)
    {
        var name = $"Lex.V3.Custody.Probe.FactPopulation.{suffix}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new CustodyIntegrityException("A commissioned population resource is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record PopulationAssets(byte[] SourceBody, RoutedHttpEvidence SourceRoute, byte[][] Facts);
}
