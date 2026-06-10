using System.Text.Json;

namespace LamaERP.Mobile.WebApp.Services;

/// <summary>The signed-in user as reported by the backend (authoritative, from the JWT claims).</summary>
public sealed record WebUser(string UserId, string Name, string? Email, string? Username, string? ThumbUrl);

/// <summary>
/// Resolves who is actually signed in by calling the backend with the WebView's captured auth
/// cookies — <c>/api/auth/me</c> for identity and <c>/api/users/me</c> for the profile photo. This
/// is the reliable source for de-duplicating accounts (by user id) and for the name/email/avatar
/// shown in the account picker. Avatars are cached on disk, keyed by account id.
/// </summary>
public sealed class UserProfileService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string AvatarDir
    {
        get
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "account_avatars");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string Safe(string id) => string.Concat(id.Split(Path.GetInvalidFileNameChars()));

    public string AvatarPathFor(string accountId) => Path.Combine(AvatarDir, Safe(accountId) + ".img");

    public bool HasAvatar(string accountId) => File.Exists(AvatarPathFor(accountId));

    public ImageSource? AvatarOrNull(string accountId) =>
        HasAvatar(accountId) ? ImageSource.FromFile(AvatarPathFor(accountId)) : null;

    /// <summary>Reads the signed-in user (identity + photo URL) using the account's saved cookies.</summary>
    public async Task<WebUser?> FetchAsync(string domain, string? cookieHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)) return null;
        var origin = AppConfig.OriginFor(domain);

        var me = await GetJsonAsync(origin + "/api/auth/me", domain, cookieHeader, ct);
        if (me is null) return null;

        var userId = Str(me.Value, "userId", "UserId");
        var name = Str(me.Value, "name", "Name");
        if (string.IsNullOrEmpty(userId)) return null;

        var email = Str(me.Value, "email", "Email");
        var username = Str(me.Value, "username", "Username");

        // Profile photo is on a separate endpoint; best-effort, not fatal if it 404s.
        string? thumb = null;
        var users = await GetJsonAsync(origin + "/api/users/me", domain, cookieHeader, ct);
        if (users is not null)
            thumb = Str(users.Value, "thumbUrl", "ThumbUrl") ?? Str(users.Value, "photoUrl", "PhotoUrl");

        return new WebUser(userId!, name ?? username ?? email ?? "User", email, username, thumb);
    }

    /// <summary>Downloads the user's photo (using their cookies) and caches it for the account.
    /// Clears the cached avatar when there's no photo so the initial-letter fallback is used.</summary>
    public async Task SaveAvatarAsync(string accountId, string domain, string? thumbUrl, string? cookieHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(thumbUrl) || string.IsNullOrWhiteSpace(cookieHeader))
        {
            TryDelete(AvatarPathFor(accountId));
            return;
        }

        var origin = AppConfig.OriginFor(domain);
        var url = thumbUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? thumbUrl
            : origin + (thumbUrl.StartsWith('/') ? thumbUrl : "/" + thumbUrl);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return;

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > 0)
                await File.WriteAllBytesAsync(AvatarPathFor(accountId), bytes, ct);
        }
        catch { /* best-effort */ }
    }

    public void RemoveAvatar(string accountId) => TryDelete(AvatarPathFor(accountId));

    private async Task<JsonElement?> GetJsonAsync(string url, string domain, string cookieHeader, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            req.Headers.TryAddWithoutValidation("X-Tenant-Id", domain);
            req.Headers.TryAddWithoutValidation(AppConfig.ClientTypeHeader, AppConfig.ClientTypeValue);

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Unwrap the gateway envelope ({ data: {...} }) if present.
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                root = data;
            return root.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var s = p.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
