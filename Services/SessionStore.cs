namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Durable, encrypted backup of each account's auth cookies (access_token / refresh_token) in the
/// platform SecureStorage (Android Keystore / iOS Keychain / Windows DPAPI). The WebView keeps a
/// single shared cookie jar, so <see cref="IWebSession"/> swaps the right account's cookies in/out
/// of it on every switch — this store is the authoritative per-account copy it restores from.
///
/// Keyed by the account's stable id (NOT the host), so two users of the same tenant each keep their
/// own session.
/// </summary>
public sealed class SessionStore
{
    private static string Key(string accountId) => $"web_session_cookies::{accountId}";

    public Task<string?> LoadAsync(string accountId) => SecureStorage.Default.GetAsync(Key(accountId));

    public async Task SaveAsync(string accountId, string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            Remove(accountId);
            return;
        }
        await SecureStorage.Default.SetAsync(Key(accountId), cookieHeader);
    }

    public void Remove(string accountId) => SecureStorage.Default.Remove(Key(accountId));
}
