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

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY deploy/indexes ./indexes
ENV LEX_INDEX_DIR=/app/indexes
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lex.Web.dll"]
