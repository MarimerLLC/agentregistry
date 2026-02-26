# Contributing to AgentRegistry

Thank you for your interest in contributing. This document covers everything you need to get from zero to a merged pull request.

## Code of conduct

This project follows the [Contributor Covenant v2.1](CODE_OF_CONDUCT.md). Please read it before participating.

## Ways to contribute

- **Bug reports** — open an issue with steps to reproduce, expected behavior, and actual behavior.
- **Feature proposals** — open an issue first and describe the use case before writing code. The project has a specific scope (protocol-agnostic registry with multi-transport and ephemeral-agent support) and not every feature belongs here.
- **Pull requests** — bug fixes, tests, and documentation improvements are always welcome. For larger changes, discuss in an issue first.

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 14+ (local instance, Docker, or a remote dev cluster)
- Redis 7+ (local instance or Docker)
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`

### Clone and restore

```bash
git clone <repo-url>
cd AgentRegistry
dotnet restore
```

### Configure local secrets

Use .NET user secrets — never commit connection strings or tokens:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" \
  "Host=localhost;Port=5432;Database=agentregistry;Username=agentregistry;Password=<password>" \
  --project src/AgentRegistry.Api

dotnet user-secrets set "ConnectionStrings:Redis" \
  "localhost:6379" \
  --project src/AgentRegistry.Api
```

### Create the database

```bash
psql -U postgres -c "CREATE DATABASE agentregistry;"
psql -U postgres -d agentregistry -c "CREATE USER agentregistry WITH PASSWORD '<password>';"
psql -U postgres -d agentregistry -c "GRANT ALL ON SCHEMA public TO agentregistry;"
```

### Apply migrations

The `AGENTREGISTRY_DB` environment variable is used by the EF Core design-time factory so `dotnet ef` commands connect to a real database:

```bash
export AGENTREGISTRY_DB="Host=localhost;Database=agentregistry;Username=agentregistry;Password=<password>"
dotnet ef database update -p src/AgentRegistry.Infrastructure -s src/AgentRegistry.Api
```

`appsettings.Development.json` has `Database:AutoMigrate: true`, so running the API locally will also apply pending migrations automatically.

### Run

```bash
dotnet run --project src/AgentRegistry.Api
```

- API: `http://localhost:5000`
- Scalar explorer: `http://localhost:5000/scalar/v1`
- Liveness: `http://localhost:5000/healthz`
- Readiness: `http://localhost:5000/readyz`

### Get your first Admin key locally

```bash
# Set a bootstrap token in user secrets
dotnet user-secrets set "Bootstrap:Token" "dev-bootstrap-token" --project src/AgentRegistry.Api

# Bootstrap an Admin key
curl -X POST http://localhost:5000/api-keys/bootstrap \
  -H "X-Bootstrap-Token: dev-bootstrap-token" \
  -H "Content-Type: application/json" \
  -d '{"ownerId": "local-dev", "description": "local admin"}'
```

## Running tests

```bash
# All tests
dotnet test

# A single project
dotnet test tests/AgentRegistry.Api.Tests

# With detailed output
dotnet test --logger "console;verbosity=normal"
```

Tests are split into three projects:

| Project | Contents |
|---|---|
| `AgentRegistry.Domain.Tests` | Domain logic unit tests — pure, no infrastructure |
| `AgentRegistry.Application.Tests` | Service tests using [Rocks](https://github.com/JasonBock/Rocks) source-gen mocks |
| `AgentRegistry.Api.Tests` | HTTP integration tests via `WebApplicationFactory` with in-memory fakes |

Integration tests use in-memory repositories and a fake API key service — no real database or Redis is needed to run them.

## Project structure

```
src/
  AgentRegistry.Domain/           Pure domain types — zero external dependencies
    Agents/                       Agent, Endpoint, Capability, enums
    ApiKeys/                      ApiKey, ApiKeyScope
  AgentRegistry.Application/      Interfaces and use-case services
    Agents/                       AgentService, IAgentRepository, ILivenessStore
    Auth/                         IApiKeyService
  AgentRegistry.Infrastructure/   Concrete implementations
    Auth/                         SqlApiKeyService, NotImplementedApiKeyService
    Liveness/                     RedisLivenessStore, health checks
    Persistence/                  AgentRegistryDbContext, SqlAgentRepository
  AgentRegistry.Api/              ASP.NET Core 10 host
    Agents/                       Registration and discovery endpoints
    ApiKeys/                      Key management and bootstrap endpoints
    Auth/                         ApiKeyAuthenticationHandler, policies, claims
tests/
  AgentRegistry.Domain.Tests/
  AgentRegistry.Application.Tests/
  AgentRegistry.Api.Tests/
    Infrastructure/               WebApplicationFactory, in-memory fakes
k8s/
  redis.yaml                      Redis deployment for the cluster
  agentregistry/                  Service deployment manifests
```

## Making changes

### Domain and application layer

The domain has no external dependencies. Application interfaces (`IAgentRepository`, `ILivenessStore`, `IApiKeyService`) are defined in the Application project and implemented in Infrastructure. Keep the dependency direction pointing inward — Domain ← Application ← Infrastructure ← Api.

### Adding a new transport type

1. Add a value to `TransportType` in `AgentRegistry.Domain/Agents/TransportType.cs`.
2. Update `AgentRegistryDbContext` if any index or constraint references transport.
3. Add a migration.
4. Update the `AgentSearchFilter` and `InMemoryAgentRepository` if the new transport needs special filtering.

### Adding a new protocol type

1. Add a value to `ProtocolType` in `AgentRegistry.Domain/Agents/ProtocolType.cs`.
2. Protocol-specific metadata (tool manifests, agent cards, etc.) is stored as JSON in `Endpoint.ProtocolMetadata` — no schema change needed for the metadata itself.

### Adding a database migration

```bash
export AGENTREGISTRY_DB="Host=localhost;Database=agentregistry;..."
dotnet ef migrations add <DescriptiveName> \
  -p src/AgentRegistry.Infrastructure \
  -s src/AgentRegistry.Api
```

Migrations live in `src/AgentRegistry.Infrastructure/Migrations/`. Always review the generated migration before committing — check that `Up` and `Down` are correct, that indexes are named following the existing `ix_<table>_<columns>` convention, and that any new columns have appropriate defaults.

### Writing tests

**Domain tests** — plain xUnit, no mocks needed.

**Application service tests** — use [Rocks](https://github.com/JasonBock/Rocks) for mocking interfaces. Add `[assembly: Rock(typeof(IYourInterface), BuildType.Create)]` at the top of the test file and use `new IYourInterfaceCreateExpectations()` in tests.

**Integration tests** — extend or add to the `AgentRegistryFactory`-based tests in `AgentRegistry.Api.Tests`. The factory replaces all infrastructure with in-memory fakes. Use `CreateAdminClient()` for operations requiring Admin scope and `CreateAgentClient()` for Agent scope.

## Pull request process

1. Fork the repository and create a branch from `main`.
2. Write or update tests covering your change. PRs that reduce test coverage will not be merged.
3. Ensure `dotnet test` passes cleanly.
4. Ensure `dotnet build` produces no warnings (warnings are not treated as errors in CI yet, but they will be).
5. Describe what your PR changes and why in the PR description. Reference any related issues.
6. A maintainer will review within a reasonable time. Be prepared for feedback and iteration.

## Commit style

Use short, imperative present-tense commit messages:

```
Add queue-backed liveness TTL renewal
Fix discovery filter when agent has no endpoints
Update EF Core mapping for new transport types
```

One logical change per commit. Squash fixup commits before marking a PR ready for review.

## Licensing

By submitting a pull request you agree that your contribution will be licensed under the [MIT License](LICENSE) that covers this project.
