using Android.Webkit;
using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Platforms.Android;

/// <summary>
/// Android cookie bridge. <see cref="CookieManager"/> is process-global and exposes httpOnly
/// cookies to native code, so no WebView instance is required.
/// </summary>
public sealed class AndroidWebSession : IWebSession
{
    private readonly SessionStore _store;

    public AndroidWebSession(SessionStore store) => _store = store;

    public async Task RestoreAsync(string url)
    {
        var saved = await _store.LoadAsync(HostOf(url));
        if (string.IsNullOrEmpty(saved)) return;

        var cm = CookieManager.Instance;
        if (cm is null) return;

        cm.SetAcceptCookie(true);
        foreach (var pair in saved.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            cm.SetCookie(url, pair);
        cm.Flush();
    }

    public Task CaptureAsync(string url)
    {
        var cm = CookieManager.Instance;
        // Returns the full "name=value; name2=value2" header, httpOnly cookies included.
        var cookie = cm?.GetCookie(url);
        return _store.SaveAsync(HostOf(url), cookie);
    }

    public Task ClearAsync(string url)
    {
        var cm = CookieManager.Instance;
        if (cm is not null)
        {
            // Expire the auth cookies for this origin, then flush.
            cm.SetCookie(url, "access_token=; Max-Age=0; Path=/");
            cm.SetCookie(url, "refresh_token=; Max-Age=0; Path=/");
            cm.Flush();
        }
        _store.Remove(HostOf(url));
        return Task.CompletedTask;
    }

    private static string HostOf(string url) => new Uri(url).Host;
}
