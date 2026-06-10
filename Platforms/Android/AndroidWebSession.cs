using Android.Webkit;
using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Platforms.Android;

/// <summary>
/// Android cookie bridge. <see cref="CookieManager"/> is process-global and exposes httpOnly
/// cookies to native code, so no WebView instance is required. The single jar holds one account's
/// session at a time; this swaps the active account's cookies in/out, keyed by account id.
/// </summary>
public sealed class AndroidWebSession : IWebSession
{
    private readonly SessionStore _store;

    public AndroidWebSession(SessionStore store) => _store = store;

    public async Task SwitchToAsync(string accountId, string url)
    {
        var cm = CookieManager.Instance;
        if (cm is null) return;

        cm.SetAcceptCookie(true);
        ExpireHostCookies(cm, url);

        var saved = await _store.LoadAsync(accountId);
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (var pair in Split(saved))
                cm.SetCookie(url, pair + "; Path=/");
        }
        cm.Flush();
    }

    public Task CaptureAsync(string accountId, string url)
    {
        var cm = CookieManager.Instance;
        // Returns the full "name=value; name2=value2" header, httpOnly cookies included.
        var cookie = cm?.GetCookie(url);
        // Never overwrite a saved session with a logged-out/empty jar (e.g. on the login page).
        if (!AuthCookies.HasSession(cookie)) return Task.CompletedTask;
        return _store.SaveAsync(accountId, cookie);
    }

    public Task ClearAsync(string accountId, string url)
    {
        var cm = CookieManager.Instance;
        if (cm is not null)
        {
            ExpireHostCookies(cm, url);
            cm.Flush();
        }
        _store.Remove(accountId);
        return Task.CompletedTask;
    }

    // Expire every cookie currently set for this host so the next account starts from a clean jar.
    private static void ExpireHostCookies(CookieManager cm, string url)
    {
        var existing = cm.GetCookie(url);
        if (string.IsNullOrEmpty(existing)) return;
        foreach (var pair in Split(existing))
        {
            var idx = pair.IndexOf('=');
            var name = idx > 0 ? pair[..idx] : pair;
            cm.SetCookie(url, $"{name}=; Max-Age=0; Path=/");
        }
    }

    private static string[] Split(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
