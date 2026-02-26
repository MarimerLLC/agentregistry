using System.Security.Claims;
using AgentRegistry.Api.ApiKeys.Models;
using AgentRegistry.Api.Auth;
using AgentRegistry.Application;
using AgentRegistry.Application.Auth;
using AgentRegistry.Domain.ApiKeys;
using Microsoft.AspNetCore.Mvc;

namespace AgentRegistry.Api.ApiKeys;

public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api-keys");

        // All key management is Admin-only.
        group.MapPost("/", IssueKey).RequireAuthorization(RegistryPolicies.AdminOnly).WithName("IssueApiKey");
        group.MapGet("/", ListKeys).RequireAuthorization(RegistryPolicies.AdminOnly).WithName("ListApiKeys");
        group.MapDelete("/{keyId}", RevokeKey).RequireAuthorization(RegistryPolicies.AdminOnly).WithName("RevokeApiKey");

        // Bootstrap — no auth; dark unless Bootstrap:Token is set in configuration.
        // Always issues an Admin-scoped key. Use once to create the first key, then remove the token from config.
        group.MapPost("/bootstrap", Bootstrap).WithName("BootstrapApiKey");

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
