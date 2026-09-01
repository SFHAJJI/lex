using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Core;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceObjectKeyRef
{
    [JsonConstructor]
    public SourceObjectKeyRef(
        SourceRegistryMemberRef entityKind,
        string publisherUri,
        string canonicalKey,
        string canonicalKeySha256)
    {
        EntityKind = entityKind ?? throw new ArgumentNullException(nameof(entityKind));
        PublisherUri = SourceCoreValidation.RequirePublisherUri(publisherUri, nameof(publisherUri));
        CanonicalKey = SourceCoreValidation.RequireCanonicalKey(
            canonicalKey,
            canonicalKeySha256,
            nameof(canonicalKey),
            nameof(canonicalKeySha256));
        CanonicalKeySha256 = canonicalKeySha256;
    }

    public SourceRegistryMemberRef EntityKind { get; }

    public string PublisherUri { get; }

    public string CanonicalKey { get; }

    public string CanonicalKeySha256 { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceObjectRef
{
    [JsonConstructor]
    public SourceObjectRef(
        string schema,
        SourceAuthority authority,
        SourceRegistryMemberRef entityKind,
        string publisherUri,
        string canonicalKey,
        string canonicalKeySha256,
        SourceArtifactRef identityProfileRef,
        SourceObjectKeyRef? parentKeyRef)
    {
        if (!string.Equals(schema, SourceCoreSchemaIds.SourceObjectRef, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A source object reference must declare {SourceCoreSchemaIds.SourceObjectRef}.",
                nameof(schema));
        }

        Schema = schema;
        Authority = SourceCoreValidation.RequireDefined(authority, nameof(authority));
        EntityKind = entityKind ?? throw new ArgumentNullException(nameof(entityKind));
        PublisherUri = SourceCoreValidation.RequirePublisherUri(publisherUri, nameof(publisherUri));
        CanonicalKey = SourceCoreValidation.RequireCanonicalKey(
            canonicalKey,
            canonicalKeySha256,
            nameof(canonicalKey),
            nameof(canonicalKeySha256));
        CanonicalKeySha256 = canonicalKeySha256;
        IdentityProfileRef = identityProfileRef
            ?? throw new ArgumentNullException(nameof(identityProfileRef));

        if (parentKeyRef is not null)
        {
            if (parentKeyRef.EntityKind.RegistryRef != entityKind.RegistryRef)
            {
                throw new ArgumentException(
                    "A parent key must use the same source-specific entity-kind registry.",
                    nameof(parentKeyRef));
            }

            if (parentKeyRef.EntityKind == entityKind &&
                string.Equals(parentKeyRef.PublisherUri, publisherUri, StringComparison.Ordinal) &&
                string.Equals(parentKeyRef.CanonicalKey, canonicalKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A source object cannot name its own key as its parent.",
                    nameof(parentKeyRef));
            }
        }

        ParentKeyRef = parentKeyRef;
    }

    public string Schema { get; }

    public SourceAuthority Authority { get; }

    public SourceRegistryMemberRef EntityKind { get; }

    public string PublisherUri { get; }

    public string CanonicalKey { get; }

    public string CanonicalKeySha256 { get; }

    public SourceArtifactRef IdentityProfileRef { get; }

    public SourceObjectKeyRef? ParentKeyRef { get; }
}
