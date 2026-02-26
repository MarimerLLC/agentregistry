using System.Security.Claims;
using MarimerLLC.AgentRegistry.Api.ApiKeys.Models;
using MarimerLLC.AgentRegistry.Api.Auth;
using MarimerLLC.AgentRegistry.Application;
using MarimerLLC.AgentRegistry.Application.Auth;
using MarimerLLC.AgentRegistry.Domain.ApiKeys;
using Microsoft.AspNetCore.Mvc;

namespace MarimerLLC.AgentRegistry.Api.ApiKeys;

public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api-keys").WithTags("API Keys");

        // All key management is Admin-only.
        group.MapPost("/", IssueKey)
            .RequireAuthorization(RegistryPolicies.AdminOnly)
            .WithName("IssueApiKey")
            .WithSummary("Issue a new API key")
            .WithDescription("Creates a new API key for the authenticated owner. The raw key is returned exactly once in the response and cannot be retrieved again — store it immediately.")
            .Produces<IssueApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListKeys)
            .RequireAuthorization(RegistryPolicies.AdminOnly)
            .WithName("ListApiKeys")
            .WithSummary("List API keys")
            .WithDescription("Returns all active and revoked API keys owned by the authenticated principal. Raw key values are never returned.")
            .Produces<IEnumerable<ApiKeyResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{keyId}", RevokeKey)
            .RequireAuthorization(RegistryPolicies.AdminOnly)
            .WithName("RevokeApiKey")
            .WithSummary("Revoke an API key")
            .WithDescription("Marks an API key as revoked. Revoked keys are immediately rejected at authentication. Only the owning principal may revoke their own keys.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Bootstrap — no auth; dark unless Bootstrap:Token is set in configuration.
        // Always issues an Admin-scoped key. Use once to create the first key, then remove the token from config.
        group.MapPost("/bootstrap", Bootstrap)
            .WithName("BootstrapApiKey")
            .WithSummary("Issue the first Admin key (bootstrap)")
            .WithDescription(
                "Issues an Admin-scoped API key without requiring prior authentication. " +
                "This endpoint is only active when `Bootstrap:Token` is set in configuration — it returns 404 otherwise.\n\n" +
                "Supply the configured token in the `X-Bootstrap-Token` header. " +
                "Use this once to obtain the first Admin key for a new deployment, " +
                "then remove `Bootstrap:Token` from configuration to permanently disable the endpoint.\n\n" +
                "The raw key is returned exactly once.")
            .Produces<IssueApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> IssueKey(
        IssueApiKeyRequest request,
        IApiKeyService apiKeyService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (rawKey, keyId) = await apiKeyService.IssueAsync(ownerId, request.Description, request.Scope, ct);
        var keys = await apiKeyService.ListAsync(ownerId, ct);
        var info = keys.First(k => k.Id == keyId);
        return Results.Created($"/api-keys/{keyId}", IssueApiKeyResponse.From(rawKey, info));
    }

    private static async Task<IResult> ListKeys(
        IApiKeyService apiKeyService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var keys = await apiKeyService.ListAsync(ownerId, ct);
        return Results.Ok(keys.Select(ApiKeyResponse.From));
    }

    private static async Task<IResult> RevokeKey(
        string keyId,
        IApiKeyService apiKeyService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            await apiKeyService.RevokeAsync(keyId, ownerId, ct);
            return Results.NoContent();
        }
        catch (NotFoundException) { return Results.NotFound(); }
        catch (ForbiddenException) { return Results.Forbid(); }
    }

    private static async Task<IResult> Bootstrap(
        [FromHeader(Name = "X-Bootstrap-Token")] string? bootstrapToken,
        BootstrapApiKeyRequest request,
        IApiKeyService apiKeyService,
        IConfiguration config,
        CancellationToken ct)
    {
        var configuredToken = config["Bootstrap:Token"];

        // Endpoint is dark (404) unless Bootstrap:Token is explicitly set in configuration.
        // Set it in a Kubernetes secret, issue the first Admin key, then remove it.
        if (string.IsNullOrWhiteSpace(configuredToken))
            return Results.NotFound();

        if (bootstrapToken != configuredToken)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.OwnerId))
            return Results.BadRequest("OwnerId is required.");

        // Bootstrap always creates an Admin key — that's the whole point.
        var (rawKey, keyId) = await apiKeyService.IssueAsync(
            request.OwnerId, request.Description, ApiKeyScope.Admin, ct);

        var keys = await apiKeyService.ListAsync(request.OwnerId, ct);
        var info = keys.First(k => k.Id == keyId);
        return Results.Created($"/api-keys/{keyId}", IssueApiKeyResponse.From(rawKey, info));
    }
}
