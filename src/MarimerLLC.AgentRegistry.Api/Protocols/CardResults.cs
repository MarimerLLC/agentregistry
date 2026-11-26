using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarimerLLC.AgentRegistry.Api.Protocols.MCP.Models;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace MarimerLLC.AgentRegistry.Api.Protocols;

/// <summary>
/// Responses for public discovery documents (MCP Server Cards, the AI Catalog, the registry's
/// A2A card): CORS for browser clients, <c>Cache-Control</c>, a strong ETag, and
/// <c>304 Not Modified</c> when <c>If-None-Match</c> matches.
/// </summary>
public static class CardResults
{
    /// <summary>Cache lifetime for cards built from registry entries — liveness changes often.</summary>
    public const int RegistryEntryMaxAgeSeconds = 60;

    /// <summary>Cache lifetime for documents describing the registry itself.</summary>
    public const int SelfCardMaxAgeSeconds = 3600;

    public static IResult Json<T>(T value, string contentType, int maxAgeSeconds) =>
        new CardResult<T>(value, contentType, maxAgeSeconds);

    /// <summary>
    /// <c>application/mcp-server-card+json</c> when the client's <c>Accept</c> asks for it,
    /// otherwise <c>application/json</c>.
    /// </summary>
    public static string NegotiateServerCardType(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;
        // q=0 means "not acceptable", so it does not select the card media type.
        return accept.Any(a =>
                a.MediaType.Equals(ServerCardConstants.MediaType, StringComparison.OrdinalIgnoreCase) &&
                a.Quality is null or > 0)
            ? ServerCardConstants.MediaType
            : "application/json";
    }

    /// <summary>Answer a CORS preflight for a discovery document route.</summary>
    public static IResult Preflight() => new PreflightResult();

    internal static void AddCorsHeaders(HttpResponse response)
    {
        var h = response.Headers;
        h.AccessControlAllowOrigin = "*";
        h.AccessControlAllowMethods = "GET";
        h.AccessControlAllowHeaders = "Content-Type, If-None-Match";
        h.AccessControlExposeHeaders = "ETag";
    }

    private sealed class CardResult<T>(T value, string contentType, int maxAgeSeconds) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var options = httpContext.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions
                ?? JsonSerializerOptions.Web;
            var body = JsonSerializer.SerializeToUtf8Bytes(value, options);

            // The ETag covers the representation, so the media type is part of the hash.
            var hash = SHA256.HashData([.. Encoding.UTF8.GetBytes(contentType + "\n"), .. body]);
            var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(hash)}\"");

            var response = httpContext.Response;
            AddCorsHeaders(response);
            response.Headers.ETag = etag.ToString();
            response.Headers.CacheControl = $"public, max-age={maxAgeSeconds}";
            response.Headers.Vary = "Accept";

            var ifNoneMatch = httpContext.Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(etag, useStrongComparison: false)))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = contentType;
            response.ContentLength = body.Length;
            await response.Body.WriteAsync(body, httpContext.RequestAborted);
        }
    }

    private sealed class PreflightResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            AddCorsHeaders(httpContext.Response);
            httpContext.Response.Headers.AccessControlMaxAge = "86400";
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }
    }
}
