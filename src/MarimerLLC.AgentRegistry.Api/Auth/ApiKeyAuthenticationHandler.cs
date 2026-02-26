using System.Security.Claims;
using System.Text.Encodings.Web;
using MarimerLLC.AgentRegistry.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MarimerLLC.AgentRegistry.Api.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";
}

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeyService)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.NoResult();

        var result = await apiKeyService.ValidateAsync(rawKey!);
        if (!result.IsValid || result.OwnerId is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.OwnerId),
            new Claim("key_id", result.KeyId ?? string.Empty),
            new Claim("auth_method", "api_key"),
            new Claim(RegistryClaims.Scope, result.Scope?.ToString() ?? string.Empty),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
