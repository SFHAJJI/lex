# The workspace bundle is built here rather than committed: a checked-in artifact drifts
# from its source and nobody notices until the UI is wrong.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=web /src/Lex.Web/wwwroot/app ./src/Lex.Web/wwwroot/app
RUN dotnet publish src/Lex.Web -c Release -o /app

# The indexes come from the nightly's own published output, not from a developer's disk.
# See deploy/fetch-indexes.sh for why. Kept as a separate stage so the ~950 MB never enters
# the final image layer graph twice, and so a failed fetch fails the build loudly.
FROM alpine:3 AS indexes
RUN apk add --no-cache curl
COPY deploy/fetch-indexes.sh /fetch-indexes.sh
RUN sh /fetch-indexes.sh /indexes

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=indexes /indexes ./indexes
ENV LEX_INDEX_DIR=/app/indexes
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lex.Web.dll"]
