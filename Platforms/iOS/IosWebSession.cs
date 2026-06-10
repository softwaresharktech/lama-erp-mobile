using Foundation;
using LamaERP.Mobile.WebApp.Services;
using WebKit;

namespace LamaERP.Mobile.WebApp.Platforms.iOS;

/// <summary>
/// iOS cookie bridge over the default <see cref="WKWebsiteDataStore"/> cookie store, which the
/// app's WKWebView shares. Exposes httpOnly cookies to native code, so no WebView instance is
/// required. The single store holds one account's session at a time; this swaps the active
/// account's cookies in/out, keyed by account id.
/// </summary>
public sealed class IosWebSession : IWebSession
{
    private readonly SessionStore _store;

    public IosWebSession(SessionStore store) => _store = store;

    private static WKHttpCookieStore CookieStore => WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;

    public async Task SwitchToAsync(string accountId, string url)
    {
        var host = HostOf(url);
        await ClearHostAsync(host);

        var saved = await _store.LoadAsync(accountId);
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

            await SetCookieAsync(new NSHttpCookie(props));
        }
    }

    public async Task CaptureAsync(string accountId, string url)
    {
        var host = HostOf(url);
        var cookies = await GetAllAsync();
        var header = string.Join("; ", cookies
            .Where(c => DomainMatches(host, c.Domain))
            .Select(c => $"{c.Name}={c.Value}"));
        // Never overwrite a saved session with a logged-out/empty jar (e.g. on the login page).
        if (!AuthCookies.HasSession(header)) return;
        await _store.SaveAsync(accountId, header);
    }

    public async Task ClearAsync(string accountId, string url)
    {
        await ClearHostAsync(HostOf(url));
        _store.Remove(accountId);
    }

    private async Task ClearHostAsync(string host)
    {
        var cookies = await GetAllAsync();
        foreach (var c in cookies.Where(c => DomainMatches(host, c.Domain)))
            await DeleteCookieAsync(c);
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
