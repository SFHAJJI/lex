using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum MachineRequestBundleFailureReason
{
    BoundArtifactMismatch = 1,
}

public sealed class MachineRequestBundleException : InvalidOperationException
{
    internal MachineRequestBundleException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public MachineRequestBundleFailureReason Reason =>
        MachineRequestBundleFailureReason.BoundArtifactMismatch;
}

public static class HttpObservationIdentity
{
    public const string CanonicalizationIdentity = "http-observation-canonical-json/1";

    public static SourceArtifactRef Create(HttpObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new SourceArtifactRef(
            observation.ObservationId,
            MachineQueryValidation.Sha256(GetCanonicalBytes(observation)));
    }

    public static byte[] GetCanonicalBytes(HttpObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return ContractCanonicalizer.Canonicalize<HttpObservation>(
            observation,
            CanonicalizationIdentity,
            maximumDepth: 128);
    }

    public static void Validate(SourceArtifactRef artifactRef, HttpObservation observation)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        ArgumentNullException.ThrowIfNull(observation);
        if (artifactRef != Create(observation))
        {
            throw new ArgumentException(
                "The HTTP-observation artifact reference does not bind the canonical observation bytes.",
                nameof(artifactRef));
        }
    }
}

public static class MachineRequestEvidenceBundle
{
    public static MachineRequestEvidence Create(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        BoundMachineRequest sentRequest,
        SourceArtifactRef rerenderReceiptRef,
        HttpObservation httpObservation)
    {
        ArgumentNullException.ThrowIfNull(sentRequest);
        ArgumentNullException.ThrowIfNull(httpObservation);
        var rerenderReceipt = sentRequest.RenderReceipt;
        ValidateBoundRequest(sentRequest);
        ValidateArtifacts(
            plan,
            queryPlanRef,
            rerenderReceipt,
            rerenderReceiptRef,
            httpObservation);
        var evidence = MachineRequestEvidence.FromReceipt(
            queryPlanRef,
            rerenderReceiptRef,
            rerenderReceipt,
            HttpObservationIdentity.Create(httpObservation));
        ValidateRetained(
            plan,
            queryPlanRef,
            rerenderReceipt,
            rerenderReceiptRef,
            httpObservation,
            evidence);
        return evidence;
    }

    public static void ValidateRetained(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryRenderReceipt rerenderReceipt,
        SourceArtifactRef rerenderReceiptRef,
        HttpObservation httpObservation,
        MachineRequestEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(queryPlanRef);
        ArgumentNullException.ThrowIfNull(rerenderReceipt);
        ArgumentNullException.ThrowIfNull(rerenderReceiptRef);
        ArgumentNullException.ThrowIfNull(httpObservation);
        ArgumentNullException.ThrowIfNull(evidence);

        ValidateArtifacts(
            plan,
            queryPlanRef,
            rerenderReceipt,
            rerenderReceiptRef,
            httpObservation);
        ValidateReference(evidence.HttpObservationRef, httpObservation);

        var tupleMatches =
            evidence.QueryPlanRef == queryPlanRef &&
            string.Equals(evidence.QueryPlanSchema, MachineQueryPlan.SchemaId, StringComparison.Ordinal) &&
            evidence.RerenderReceiptRef == rerenderReceiptRef &&
            evidence.RequestTargetLength == rerenderReceipt.RequestTargetLength &&
            string.Equals(
                evidence.RequestTargetSha256,
                rerenderReceipt.RequestTargetSha256,
                StringComparison.Ordinal) &&
            evidence.RequestBodyLength == rerenderReceipt.RequestBodyLength &&
            string.Equals(
                evidence.RequestBodySha256,
                rerenderReceipt.RequestBodySha256,
                StringComparison.Ordinal);

        if (!tupleMatches)
        {
            throw Mismatch();
        }
    }

    private static void ValidateArtifacts(
        MachineQueryPlan plan,
        SourceArtifactRef queryPlanRef,
        MachineQueryRenderReceipt rerenderReceipt,
        SourceArtifactRef rerenderReceiptRef,
        HttpObservation httpObservation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(queryPlanRef);
        ArgumentNullException.ThrowIfNull(rerenderReceipt);
        ArgumentNullException.ThrowIfNull(rerenderReceiptRef);
        ArgumentNullException.ThrowIfNull(httpObservation);

        try
        {
            MachineQueryPlanIdentity.Validate(queryPlanRef, plan);
            MachineQueryRenderReceiptIdentity.Validate(rerenderReceiptRef, rerenderReceipt);
        }
        catch (ArgumentException exception)
        {
            throw Mismatch(exception);
        }

        var request = httpObservation.Request;
        var tupleMatches =
            rerenderReceipt.QueryPlanRef == queryPlanRef &&
            string.Equals(
                rerenderReceipt.QueryPlanSchema,
                MachineQueryPlan.SchemaId,
                StringComparison.Ordinal) &&
            rerenderReceipt.RendererProfileRef == plan.RendererProfileRef &&
            rerenderReceipt.RendererSourceRef == plan.RendererSourceRef &&
            rerenderReceipt.OrderedParameterSetRef == plan.OrderedParameterSet &&
            rerenderReceipt.ContentType == plan.ContentType &&
            rerenderReceipt.Charset == plan.Charset &&
            rerenderReceipt.InputMode == plan.InputMode &&
            rerenderReceipt.Method == plan.Method &&
            rerenderReceipt.RequestTargetLength == plan.ExpectedRequestTargetLength &&
            string.Equals(
                rerenderReceipt.RequestTargetSha256,
                plan.ExpectedRequestTargetSha256,
                StringComparison.Ordinal) &&
            rerenderReceipt.RequestBodyLength == plan.ExpectedRequestBodyLength &&
            string.Equals(
                rerenderReceipt.RequestBodySha256,
                plan.ExpectedRequestBodySha256,
                StringComparison.Ordinal) &&
            MachineQueryValidation.IsTargetBoundToPlan(plan, request.RequestedUri) &&
            request.RenderReceipt == rerenderReceipt &&
            request.Method == plan.Method;

        if (!tupleMatches)
        {
            throw Mismatch();
        }
    }

    private static void ValidateBoundRequest(BoundMachineRequest sentRequest)
    {
        try
        {
            _ = sentRequest.CopyVerifiedRequestBody();
        }
        catch (ArgumentException exception)
        {
            throw Mismatch(exception);
        }
    }

    private static void ValidateReference(
        SourceArtifactRef observationRef,
        HttpObservation httpObservation)
    {
        try
        {
            HttpObservationIdentity.Validate(observationRef, httpObservation);
        }
        catch (ArgumentException exception)
        {
            throw Mismatch(exception);
        }
    }

    private static MachineRequestBundleException Mismatch(Exception? innerException = null) => new(
        "The machine request artifacts do not bind one exact request tuple.",
        innerException);
}
