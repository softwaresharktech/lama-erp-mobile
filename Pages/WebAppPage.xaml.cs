using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

public partial class WebAppPage : ContentPage
{
    private readonly AccountStore _accounts;
    private readonly IWebSession _session;
    private readonly OrgBranding _branding;

    private string _loadedDomain = string.Empty;
    private string _url = string.Empty;
    private bool _loadingAccount;
    private bool _loggingOut;

    public WebAppPage(AccountStore accounts, IWebSession session, OrgBranding branding)
    {
        InitializeComponent();
        _accounts = accounts;
        _session = session;
        _branding = branding;
    }

    private string SelectorRoute => _accounts.HasAny ? "//accounts" : "//identifier";

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // An explicit request (switch / add / re-enter) wins over whatever is showing.
        var pending = _accounts.PendingDomain;
        var forceLogin = _accounts.PendingForceLogin;
        _accounts.PendingDomain = null;
        _accounts.PendingForceLogin = false;

        if (pending is not null)
        {
            await LoadAccountAsync(pending, forceLogin);
            return;
        }

        // First appearance with no explicit target: resume the last-used account, or fall back
        // to the selector / identifier if nothing is selected. Don't reload on incidental
        // re-appears (returning from the selector with no pick, app resume, etc.).
        if (string.IsNullOrEmpty(_loadedDomain))
        {
            if (_accounts.Active is { } account)
                await LoadAccountAsync(account.Domain);
            else
                await Shell.Current.GoToAsync(SelectorRoute, animate: false);
        }
    }

    private async Task LoadAccountAsync(string domain, bool forceLogin = false)
    {
        var previousUrl = _url;
        _loadedDomain = domain;
        _url = AppConfig.TenantWebUrl(domain);
        _loggingOut = false;

        // Drop a stale pending sign-in from an abandoned identifier entry for a different org.
        if (_accounts.PendingAccount is { } pa && !string.Equals(pa.Domain, domain, StringComparison.OrdinalIgnoreCase))
            _accounts.PendingAccount = null;

        // Cover the WebView while the new org loads so the previous account's page never flashes,
        // showing this org's own logo (falls back to the LamaERP wordmark).
        OverlayLogo.Source = _branding.LogoOrDefault(domain);
        _loadingAccount = true;
        ShowLoadOverlay(true);

        // Safety net in case the "ready" signal is missed.
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(8), () => { if (_loadingAccount) EndAccountLoad(); });

        if (forceLogin)
        {
            await _session.ClearAsync(_url);          // fresh login for this tenant (e.g. different user)
            await ClearNativeCookiesAsync(_url);      // and actually empty the native cookie jar (Windows no-ops otherwise)
        }
        else
        {
            await _session.RestoreAsync(_url);        // resume the saved session
        }

        // Re-assigning the same URL won't re-navigate, so the already-rendered SPA would keep the
        // previous user's session on screen. When reloading the same origin (e.g. adding a second
        // user of a tenant that's already shown), force a fresh bootstrap now that cookies are gone.
        if (forceLogin && string.Equals(previousUrl, _url, StringComparison.OrdinalIgnoreCase))
            Web.Reload();
        else
            Web.Source = _url;

        // Make sure this org's logo is cached so the cover shows it (anonymous, best-effort).
        _ = EnsureLogoAsync(domain);

        // Detect when the SPA settles on a real screen (it routes client-side, so no navigation
        // event fires) to drop the cover quickly and set the right chrome.
        _ = PollSettleAsync(domain);
    }

    // Polls the SPA's current path (client-side routing fires no WebView navigation event) until it
    // reaches a real screen, then applies chrome and lifts the loading cover.
    private async Task PollSettleAsync(string domain)
    {
        for (var i = 0; i < 50 && _loadingAccount && string.Equals(_loadedDomain, domain, StringComparison.OrdinalIgnoreCase); i++)
        {
            await Task.Delay(250);
            string? path;
            try { path = await Web.EvaluateJavaScriptAsync("location.pathname"); }
            catch { path = null; }
            if (string.IsNullOrEmpty(path)) continue;

            path = path.Trim('"').ToLowerInvariant();
            if (IsAuthPath(path) || IsPortalPath(path))
            {
                ApplyChrome(path);
                EndAccountLoad();
                return;
            }
        }
    }

    private async Task EnsureLogoAsync(string domain)
    {
        if (_branding.HasLogoForDomain(domain)) return; // already cached — keep switching fast
        if (await _branding.RefreshAsync(domain) &&
            _loadingAccount && string.Equals(_loadedDomain, domain, StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.Dispatch(() => OverlayLogo.Source = _branding.LogoOrDefault(domain));
        }
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Intercept native commands the web triggers (switch account / logout / ready).
        if (e.Url.StartsWith(WebBridge.Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            HandleAppCommand(e.Url);
            return;
        }

        ShowBusy(true);
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        ShowBusy(false);

        // Apply chrome for full navigations (login -> /portal, logout redirects, etc.).
        if (TryGetPath(e.Url, out var path))
            ApplyChrome(path);

        // Baseline cover-drop once the document has loaded — polling lifts it sooner when the SPA
        // settles client-side; this guarantees it never sticks.
        if (_loadingAccount && e.Result == WebNavigationResult.Success)
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => { if (_loadingAccount) EndAccountLoad(); });

        if (e.Result != WebNavigationResult.Success)
            return;

        // Tag this client as mobile for all API calls + expose window.LamaMobile / ready signal.
        try { await Web.EvaluateJavaScriptAsync(WebBridge.InjectClientTypeJs); }
        catch { /* injection is best-effort */ }

        // Mirror the current (possibly just-issued) auth cookies into SecureStorage.
        try { await _session.CaptureAsync(_url); }
        catch { /* capture is best-effort */ }
    }

    private void HandleAppCommand(string url)
    {
        Uri.TryCreate(url, UriKind.Absolute, out var uri);
        switch (uri?.Host ?? string.Empty)
        {
            case "accounts":
                Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync("//accounts", animate: false));
                break;
            case "logout":
                Dispatcher.Dispatch(async () => await LogoutAsync());
                break;
        }
    }

    private async Task LogoutAsync()
    {
        if (_loggingOut) return; // the web may signal logout more than once
        _loggingOut = true;

        var domain = _loadedDomain;
        var url = _url;

        // Clear this org's session (cookie jar + SecureStorage) and forget the saved login.
        if (!string.IsNullOrEmpty(url))
        {
            try { await _session.ClearAsync(url); } catch { /* best-effort */ }
        }
        if (!string.IsNullOrEmpty(domain))
            _accounts.Remove(domain);

        _accounts.ClearActive();
        _loadedDomain = string.Empty;
        _url = string.Empty;

        // Blank the WebView so the logged-out page isn't visible behind the selector.
        Web.Source = "about:blank";

        await Shell.Current.GoToAsync(SelectorRoute, animate: false);
    }

    private async void OnCancelAuthClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync(SelectorRoute, animate: false);

    protected override bool OnBackButtonPressed()
    {
        // On a sign-in page, hardware back returns to the account selector.
        if (AuthBar.IsVisible)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(SelectorRoute, animate: false));
            return true;
        }

        // Otherwise walk the web history first; only leave the app at the root.
        if (Web.CanGoBack)
        {
            Web.GoBack();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    // Empties the native WebView cookie jar for this origin. Android/iOS clear cookies inside their
    // IWebSession.ClearAsync, but WebView2 keeps its own persistent jar that ClearAsync can't reach
    // (it has no WebView reference), so it's purged here where the control is available.
    private async Task ClearNativeCookiesAsync(string url)
    {
#if WINDOWS
        if (Web.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native)
            return;
        try
        {
            await native.EnsureCoreWebView2Async();
            var manager = native.CoreWebView2.CookieManager;
            foreach (var cookie in await manager.GetCookiesAsync(url))
                manager.DeleteCookie(cookie);
        }
        catch { /* best-effort: clearing must never crash the load */ }
#else
        await Task.CompletedTask;
#endif
    }

    private void EndAccountLoad()
    {
        _loadingAccount = false;
        ShowLoadOverlay(false);
    }

    private void ShowLoadOverlay(bool show)
    {
        LoadOverlay.IsVisible = show;
        OverlaySpinner.IsRunning = show;
    }

    private void ShowBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
    }

    private void ApplyChrome(string path)
    {
        // Cancel bar only on the sign-in pages; portal stays full-screen.
        AuthBar.IsVisible = IsAuthPath(path);

        // Reaching the portal means the user is signed into this org.
        if (IsPortalPath(path) && !string.IsNullOrEmpty(_loadedDomain))
        {
            // Commit the account to the saved list now that login succeeded (deferred from the
            // identifier step) so a cancelled sign-in never leaves a logged-out account behind.
            if (_accounts.PendingAccount is { } pending &&
                string.Equals(pending.Domain, _loadedDomain, StringComparison.OrdinalIgnoreCase))
            {
                _accounts.AddOrUpdate(pending);
                _accounts.PendingAccount = null;
            }
            _accounts.SetActive(_loadedDomain);
        }
    }

    private static bool IsAuthPath(string path)
        => path.StartsWith("/login") || path.Contains("forgot") || path.Contains("reset-password");

    private static bool IsPortalPath(string path)
        => path.StartsWith("/portal");

    private static bool TryGetPath(string? url, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        path = uri.AbsolutePath.ToLowerInvariant();
        return true;
    }
}
