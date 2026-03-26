# syntax=docker/dockerfile:1

# ──────────────────────────────────────────────────────────────
# Stage 1: build Angular frontend
# ──────────────────────────────────────────────────────────────
FROM node:22-alpine AS web-build
WORKDIR /web
COPY src/Veda.Web/package*.json ./
RUN npm ci
COPY src/Veda.Web/ ./
RUN npm run build -- --configuration production

# ──────────────────────────────────────────────────────────────
# Stage 2: restore & build .NET API
# ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution-level props so TargetFramework is resolved correctly
COPY Directory.Build.props ./

# Copy all csproj files first to leverage layer caching for restore
COPY src/Veda.Core/Veda.Core.csproj               src/Veda.Core/
COPY src/Veda.Prompts/Veda.Prompts.csproj         src/Veda.Prompts/
COPY src/Veda.Services/Veda.Services.csproj       src/Veda.Services/
COPY src/Veda.Storage/Veda.Storage.csproj         src/Veda.Storage/
COPY src/Veda.Agents/Veda.Agents.csproj           src/Veda.Agents/
COPY src/Veda.Evaluation/Veda.Evaluation.csproj   src/Veda.Evaluation/
COPY src/Veda.MCP/Veda.MCP.csproj                 src/Veda.MCP/
COPY src/Veda.Api/Veda.Api.csproj                 src/Veda.Api/

RUN dotnet restore src/Veda.Api/Veda.Api.csproj

# Copy remaining source and publish
COPY src/ src/
RUN dotnet publish src/Veda.Api/Veda.Api.csproj \
    -c Release -o /app/publish --no-restore

# ──────────────────────────────────────────────────────────────
# Stage 2: runtime
# ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Persist the SQLite database outside the container filesystem
VOLUME /app/data

COPY --from=build /app/publish .
# Copy Angular build output into wwwroot so ASP.NET Core serves the SPA
COPY --from=web-build /web/dist/veda-web/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__VedaDb=Data Source=/app/data/veda.db

EXPOSE 8080
ENTRYPOINT ["dotnet", "Veda.Api.dll"]
