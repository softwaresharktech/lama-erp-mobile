using System.Text.Json;

namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Caches each organization's logo on the device so the splash/loading screens can show it.
/// The logo URL comes from the ANONYMOUS <c>/api/auth/tenant/resolve</c> endpoint (no login
/// needed), and the image is served from the public <c>/api/files/blob</c> proxy — so the logo
/// can be cached the moment an org is entered, even before sign-in. If an org has no logo, the
/// cached file is removed and the app falls back to the LamaERP logo.
/// </summary>
public sealed class OrgBranding
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static string LogoDir
    {
        get
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "org_logos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SafeHost(string host) => string.Concat(host.Split(Path.GetInvalidFileNameChars()));

    private static string HostOf(string domain) => new Uri(AppConfig.TenantWebUrl(domain)).Host;

    public string LogoPathFor(string host) => Path.Combine(LogoDir, SafeHost(host) + ".img");

    public bool HasLogo(string host) => File.Exists(LogoPathFor(host));

    public bool HasLogoForDomain(string domain) => HasLogo(HostOf(domain));

    public ImageSource LogoOrDefault(string domain)
    {
        var host = HostOf(domain);
        return HasLogo(host) ? ImageSource.FromFile(LogoPathFor(host)) : "lama_logo.png";
    }

    /// <summary>Downloads and caches a known logo URL (from the resolve response). Clears the cache
    /// when the org has no logo so the default is used.</summary>
    public async Task<bool> SaveLogoAsync(string domain, string? logoUrl, CancellationToken ct = default)
    {
        var origin = AppConfig.OriginFor(domain);
        var host = new Uri(origin).Host;

        if (string.IsNullOrWhiteSpace(logoUrl))
        {
            TryDelete(LogoPathFor(host));
            return false;
        }

        try
        {
            var imageUrl = logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? logoUrl
                : origin + (logoUrl.StartsWith('/') ? logoUrl : "/" + logoUrl);

            var bytes = await _http.GetByteArrayAsync(imageUrl, ct);
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(LogoPathFor(host), bytes, ct);
                return true;
            }
        }
        catch { /* best-effort */ }
        return false;
    }

    /// <summary>Resolves the org (anonymously) to discover its logo URL, then caches it.</summary>
    public async Task<bool> RefreshAsync(string domain, CancellationToken ct = default)
    {
        var origin = AppConfig.OriginFor(domain);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/auth/tenant/resolve");
            req.Headers.Add("X-Tenant-Id", domain);
            req.Headers.Add(AppConfig.ClientTypeHeader, AppConfig.ClientTypeValue);

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return false;

            var json = await res.Content.ReadAsStringAsync(ct);
            return await SaveLogoAsync(domain, ReadLogoUrl(json), ct);
        }
        catch
        {
            return false;
        }
    }

    // Handles both the gateway envelope ({ data: { logoUrl } }) and a bare object.
    private static string? ReadLogoUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                root = data;

            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "logoUrl", "LogoUrl" })
                if (root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
