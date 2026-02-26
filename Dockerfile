FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and package management files first for layer caching.
COPY MarimerLLC.AgentRegistry.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .

# Copy project files for restore (src + test csproj stubs — no test source needed).
COPY src/MarimerLLC.AgentRegistry.Domain/MarimerLLC.AgentRegistry.Domain.csproj                    src/MarimerLLC.AgentRegistry.Domain/
COPY src/MarimerLLC.AgentRegistry.Application/MarimerLLC.AgentRegistry.Application.csproj          src/MarimerLLC.AgentRegistry.Application/
COPY src/MarimerLLC.AgentRegistry.Infrastructure/MarimerLLC.AgentRegistry.Infrastructure.csproj     src/MarimerLLC.AgentRegistry.Infrastructure/
COPY src/MarimerLLC.AgentRegistry.Api/MarimerLLC.AgentRegistry.Api.csproj                          src/MarimerLLC.AgentRegistry.Api/
COPY tests/AgentRegistry.Domain.Tests/AgentRegistry.Domain.Tests.csproj      tests/AgentRegistry.Domain.Tests/
COPY tests/AgentRegistry.Application.Tests/AgentRegistry.Application.Tests.csproj tests/AgentRegistry.Application.Tests/
COPY tests/AgentRegistry.Api.Tests/AgentRegistry.Api.Tests.csproj            tests/AgentRegistry.Api.Tests/

RUN dotnet restore MarimerLLC.AgentRegistry.slnx

# Copy source and publish.
COPY src/ src/
RUN dotnet publish src/MarimerLLC.AgentRegistry.Api/MarimerLLC.AgentRegistry.Api.csproj \
    --configuration Release \
    --output /app/publish

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for least-privilege execution.
RUN groupadd --system --gid 1001 agentregistry \
 && useradd  --system --uid 1001 --gid agentregistry agentregistry

COPY --from=build --chown=agentregistry:agentregistry /app/publish .

USER agentregistry

# ASP.NET Core defaults to port 8080 in container environments.
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "MarimerLLC.AgentRegistry.Api.dll"]
