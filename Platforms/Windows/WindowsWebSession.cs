using LamaERP.Mobile.WebApp.Services;
using Microsoft.UI.Xaml.Controls;

namespace LamaERP.Mobile.WebApp.Platforms.Windows;

/// <summary>
/// Windows (WebView2) cookie bridge. Unlike Android/iOS there is no process-global cookie store, so
/// the active WebView2 control is attached by the page (<see cref="View"/>) and its CoreWebView2
/// CookieManager is used to read/write the jar. WebView2 persists its jar in the per-app user-data
/// folder, so accounts of the same tenant share it — this swaps the active account's cookies in/out,
/// keyed by account id.
/// </summary>
public sealed class WindowsWebSession : IWebSession
{
    private readonly SessionStore _store;

    public WindowsWebSession(SessionStore store) => _store = store;

    /// <summary>The currently-mounted WebView2, set by the WebApp page once its handler exists.</summary>
    public WebView2? View { get; set; }

    public async Task SwitchToAsync(string accountId, string url)
    {
        var core = await CoreAsync();
        if (core is null) return;

        var manager = core.CookieManager;
        foreach (var c in await manager.GetCookiesAsync(url))
            manager.DeleteCookie(c);

        var saved = await _store.LoadAsync(accountId);
        if (string.IsNullOrEmpty(saved)) return;

        var host = new Uri(url).Host;
        var secure = url.StartsWith("https", StringComparison.OrdinalIgnoreCase);
        foreach (var pair in saved.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;

            var cookie = manager.CreateCookie(pair[..idx], pair[(idx + 1)..], host, "/");
            cookie.IsHttpOnly = true;
            cookie.IsSecure = secure;
            manager.AddOrUpdateCookie(cookie);
        }
    }

    public async Task CaptureAsync(string accountId, string url)
    {
        var core = await CoreAsync();
        if (core is null) return;

        var cookies = await core.CookieManager.GetCookiesAsync(url);
        var header = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        // Never overwrite a saved session with a logged-out/empty jar (e.g. on the login page).
        if (!AuthCookies.HasSession(header)) return;
        await _store.SaveAsync(accountId, header);
    }

    public async Task ClearAsync(string accountId, string url)
    {
        var core = await CoreAsync();
        if (core is not null)
        {
            var manager = core.CookieManager;
            foreach (var c in await manager.GetCookiesAsync(url))
                manager.DeleteCookie(c);
        }
        _store.Remove(accountId);
    }

    private async Task<Microsoft.Web.WebView2.Core.CoreWebView2?> CoreAsync()
    {
        var view = View;
        if (view is null) return null;
        try
        {
            await view.EnsureCoreWebView2Async();
            return view.CoreWebView2;
        }
        catch
        {
            return null; // control not ready yet — best-effort
        }
    }
}
