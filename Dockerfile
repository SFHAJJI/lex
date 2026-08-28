# The workspace bundle is built here rather than committed: a checked-in artifact drifts
# from its source and nobody notices until the UI is wrong.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
WORKDIR /src
COPY . .
COPY --from=web /src/Lex.Web/wwwroot/app ./src/Lex.Web/wwwroot/app
RUN dotnet restore Lex.slnx --locked-mode --nologo
RUN dotnet publish src/Lex.Web -c Release -o /app --no-restore --nologo

# The indexes come from the nightly's own published output, not from a developer's disk.
# See deploy/fetch-indexes.sh for why. Kept as a separate stage so the ~950 MB never enters
# the final image layer graph twice, and so a failed fetch fails the build loudly.
FROM alpine:3 AS indexes
RUN apk add --no-cache curl jq
ARG LEX_REQUIRE_ARTIFACT_MANIFEST=0
ARG LEX_RELEASE_TAG_LU_LEGILUX=
ARG LEX_RELEASE_TAG_EU_EURLEX=
ENV LEX_REQUIRE_ARTIFACT_MANIFEST=$LEX_REQUIRE_ARTIFACT_MANIFEST
ENV LEX_RELEASE_TAG_LU_LEGILUX=$LEX_RELEASE_TAG_LU_LEGILUX
ENV LEX_RELEASE_TAG_EU_EURLEX=$LEX_RELEASE_TAG_EU_EURLEX
COPY deploy/fetch-indexes.sh /fetch-indexes.sh
RUN sh /fetch-indexes.sh /indexes

# Verify any manifest that exists before it can enter the runtime image. Absence is permitted
# only during the migration release; an invalid signature or hash always fails this build.
FROM build AS verified-indexes
ARG LEX_REQUIRE_ARTIFACT_MANIFEST=0
ARG LEX_EXPECTED_MANIFEST_SET=
COPY --from=indexes /indexes /indexes
COPY deploy/trusted-artifact-roots.json /trust/trusted-artifact-roots.json
RUN set -eu; found=0; \
    for manifest in /indexes/*.manifest.json; do \
      [ -f "$manifest" ] || continue; found=1; \
      signature="${manifest%.json}.sig"; \
      dotnet run --project src/Lex.Ingest -c Release --no-restore -- artifact verify \
        --root /indexes --manifest "$manifest" --signature "$signature" \
        --trust-roots /trust/trusted-artifact-roots.json; \
    done; \
    if [ "${LEX_REQUIRE_ARTIFACT_MANIFEST:-0}" = "1" ] && [ "$found" = "0" ]; then \
      echo "ERROR: artifact manifests required but absent" >&2; exit 1; \
    fi; \
    if [ -n "${LEX_EXPECTED_MANIFEST_SET:-}" ] && [ "$LEX_EXPECTED_MANIFEST_SET" != "legacy" ]; then \
      actual=$(for manifest in /indexes/*.manifest.json; do \
        [ -f "$manifest" ] || continue; \
        printf '%s  %s\n' "$(sha256sum "$manifest" | cut -d' ' -f1)" "$(basename "$manifest")"; \
      done | sort -k2 | sha256sum | cut -d' ' -f1); \
      [ "$actual" = "$LEX_EXPECTED_MANIFEST_SET" ] || { \
        echo "ERROR: artifact manifest set changed during image construction" >&2; exit 1; \
      }; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94
WORKDIR /app
COPY --from=build /app .
COPY --from=verified-indexes /indexes ./indexes
ENV LEX_INDEX_DIR=/app/indexes
ENV LEX_EMBEDDING_MODEL_DIR=/app/indexes
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lex.Web.dll"]
