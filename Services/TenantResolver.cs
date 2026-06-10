using System.Net;
using System.Text.Json;

namespace LamaERP.Mobile.WebApp.Services;

public sealed record TenantResolveResult(bool Success, string? Identifier = null, string? Name = null, string? LogoUrl = null, string? Error = null)
{
    public static TenantResolveResult Ok(string identifier, string? name, string? logoUrl) => new(true, identifier, name, logoUrl);
    public static TenantResolveResult Fail(string error) => new(false, Error: error);
}

/// <summary>
/// Validates the organization the user typed by calling the backend's tenant-resolve endpoint
/// (the "find and connect to that database" step) before handing off to the WebView.
/// </summary>
public sealed class TenantResolver
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<TenantResolveResult> ResolveAsync(string domain, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppConfig.ResolveApiUrl(domain));
            // The tenant is resolved from this header; also announce ourselves as a mobile client.
            req.Headers.Add("X-Tenant-Id", domain);
            req.Headers.Add(AppConfig.ClientTypeHeader, AppConfig.ClientTypeValue);

            using var res = await _http.SendAsync(req, ct);

            if (res.StatusCode == HttpStatusCode.NotFound)
                return TenantResolveResult.Fail("We couldn't find that organization. Check the address and try again.");
            if (res.StatusCode == HttpStatusCode.Forbidden)
                return TenantResolveResult.Fail("This organization is not available right now.");
            if (!res.IsSuccessStatusCode)
                return TenantResolveResult.Fail($"Couldn't verify that organization ({(int)res.StatusCode}). Try again.");

            var json = await res.Content.ReadAsStringAsync(ct);
            if (TryReadTenant(json, out var identifier, out var name, out var logoUrl))
                return TenantResolveResult.Ok(identifier!, name, logoUrl);

            return TenantResolveResult.Fail("Couldn't verify that organization. Try again.");
        }
        catch (FormatException)
        {
            // Bad URI/hostname (e.g. a stray space or invalid character in the entered address).
            return TenantResolveResult.Fail("That doesn't look like a valid organization address. Check it and try again.");
        }
        catch (TaskCanceledException)
        {
            return TenantResolveResult.Fail("The request timed out. Check your connection and try again.");
        }
        catch (HttpRequestException)
        {
            return TenantResolveResult.Fail("Couldn't reach the server. Check your connection and try again.");
        }
        catch (Exception)
        {
            // Never let resolution crash the app — always surface a friendly error.
            return TenantResolveResult.Fail("Couldn't verify that organization. Try again.");
        }
    }

    // Handles both the gateway envelope ({ success, data:{...} }) and a bare tenant object.
    private static bool TryReadTenant(string json, out string? identifier, out string? name, out string? logoUrl)
    {
        identifier = null;
        name = null;
        logoUrl = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                root = data;
            }

            if (root.ValueKind != JsonValueKind.Object) return false;

            identifier = GetString(root, "identifier");
            name = GetString(root, "name");
            logoUrl = GetString(root, "logoUrl");
            return !string.IsNullOrEmpty(identifier);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
