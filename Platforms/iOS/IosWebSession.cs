using Foundation;
using LamaERP.Mobile.WebApp.Services;
using WebKit;

namespace LamaERP.Mobile.WebApp.Platforms.iOS;

/// <summary>
/// iOS cookie bridge over the default <see cref="WKWebsiteDataStore"/> cookie store, which the
/// app's WKWebView shares. Exposes httpOnly cookies to native code, so no WebView instance is
/// required. All operations are async with completion handlers, wrapped as Tasks here.
/// </summary>
public sealed class IosWebSession : IWebSession
{
    private readonly SessionStore _store;

    public IosWebSession(SessionStore store) => _store = store;

    private static WKHttpCookieStore CookieStore => WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;

    public async Task RestoreAsync(string url)
    {
        var host = HostOf(url);
        var saved = await _store.LoadAsync(host);
        if (string.IsNullOrEmpty(saved)) return;

        foreach (var pair in saved.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;

            var props = new NSMutableDictionary
            {
                [NSHttpCookie.KeyName] = new NSString(pair[..idx]),
                [NSHttpCookie.KeyValue] = new NSString(pair[(idx + 1)..]),
                [NSHttpCookie.KeyDomain] = new NSString(host),
                [NSHttpCookie.KeyPath] = new NSString("/"),
                [NSHttpCookie.KeySecure] = new NSString("TRUE"),
            };

            var cookie = new NSHttpCookie(props);
            await SetCookieAsync(cookie);
        }
    }

    public async Task CaptureAsync(string url)
    {
        var host = HostOf(url);
        var cookies = await GetAllAsync();
        var parts = cookies
            .Where(c => DomainMatches(host, c.Domain))
            .Select(c => $"{c.Name}={c.Value}");
        await _store.SaveAsync(host, string.Join("; ", parts));
    }

    public async Task ClearAsync(string url)
    {
        var host = HostOf(url);
        var cookies = await GetAllAsync();
        foreach (var c in cookies.Where(c => DomainMatches(host, c.Domain)))
            await DeleteCookieAsync(c);
        _store.Remove(host);
    }

    private Task SetCookieAsync(NSHttpCookie cookie)
    {
        var tcs = new TaskCompletionSource<bool>();
        CookieStore.SetCookie(cookie, () => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private Task DeleteCookieAsync(NSHttpCookie cookie)
    {
        var tcs = new TaskCompletionSource<bool>();
        CookieStore.DeleteCookie(cookie, () => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private Task<NSHttpCookie[]> GetAllAsync()
    {
        var tcs = new TaskCompletionSource<NSHttpCookie[]>();
        CookieStore.GetAllCookies(cookies => tcs.TrySetResult(cookies));
        return tcs.Task;
    }

    private static bool DomainMatches(string host, string cookieDomain)
    {
        var d = cookieDomain.TrimStart('.');
        return host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase);
    }

    private static string HostOf(string url) => new Uri(url).Host;
}
