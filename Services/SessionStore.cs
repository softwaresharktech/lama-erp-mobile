namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Durable, encrypted backup of the tenant auth cookies (access_token / refresh_token) in the
/// platform SecureStorage (Android Keystore / iOS Keychain). The WebView keeps its own cookie
/// jar, but the OS can evict it; this store is the authoritative copy that <see cref="IWebSession"/>
/// restores into the jar before each launch so the signed-in session survives.
/// </summary>
public sealed class SessionStore
{
    private static string Key(string host) => $"web_session_cookies::{host}";

    public Task<string?> LoadAsync(string host) => SecureStorage.Default.GetAsync(Key(host));

    public async Task SaveAsync(string host, string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            Remove(host);
            return;
        }
        await SecureStorage.Default.SetAsync(Key(host), cookieHeader);
    }

    public void Remove(string host) => SecureStorage.Default.Remove(Key(host));
}
