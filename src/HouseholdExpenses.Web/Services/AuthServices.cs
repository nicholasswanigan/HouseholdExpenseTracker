using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace HouseholdExpenses.Web.Services;

/// <summary>Persists the JWT in the browser's localStorage so the session survives a refresh.</summary>
public class TokenStore(IJSRuntime js)
{
    private const string Key = "household.token";
    private string? _cached;
    private bool _loaded;

    public async ValueTask<string?> GetAsync()
    {
        if (!_loaded)
        {
            _cached = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            _loaded = true;
        }
        return _cached;
    }

    public async ValueTask SetAsync(string token)
    {
        _cached = token;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", Key, token);
    }

    public async ValueTask ClearAsync()
    {
        _cached = null;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}

/// <summary>Builds the Blazor identity from the claims inside the JWT (and drops expired tokens).</summary>
public class JwtAuthStateProvider(TokenStore tokens) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await tokens.GetAsync();
        if (string.IsNullOrEmpty(token)) return Anonymous;

        var claims = TryParse(token);
        if (claims is null)
        {
            await tokens.ClearAsync();
            return Anonymous;
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", "name", ClaimTypes.Role)));
    }

    public async Task SignInAsync(string token)
    {
        await tokens.SetAsync(token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SignOutAsync()
    {
        await tokens.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    /// <summary>Returns the claims, or null if the token is malformed or expired.</summary>
    private static List<Claim>? TryParse(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

            var claims = new List<Claim>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                claims.Add(new Claim(prop.Name, prop.Value.ToString()));

            var exp = claims.FirstOrDefault(c => c.Type == "exp");
            if (exp is not null && DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp.Value)) <= DateTimeOffset.UtcNow)
                return null;

            return claims;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
