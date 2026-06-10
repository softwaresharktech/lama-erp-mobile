using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

public partial class WebAppPage : ContentPage
{
    private readonly AccountStore _accounts;
    private readonly IWebSession _session;
    private readonly SessionStore _sessionStore;
    private readonly OrgBranding _branding;
    private readonly UserProfileService _profiles;

    private string _loadedDomain = string.Empty;
    private string _url = string.Empty;
    private string? _activeAccountId;      // the account currently shown in the WebView
    private Account? _commitAccount;       // a newly-added account awaiting login success
    private bool _loadingAccount;
    private bool _loggingOut;

    public WebAppPage(AccountStore accounts, IWebSession session, SessionStore sessionStore, OrgBranding branding, UserProfileService profiles)
    {
        InitializeComponent();
        _accounts = accounts;
        _session = session;
        _sessionStore = sessionStore;
        _branding = branding;
        _profiles = profiles;

#if WINDOWS
        // Windows has no global cookie store, so hand the active WebView2 to the session as soon as
        // its handler exists; the session uses it to read/write the cookie jar.
        Web.HandlerChanged += (_, _) => AttachWindowsWebView();
#endif
    }

    private string SelectorRoute => _accounts.HasAny ? "//accounts" : "//identifier";

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // An explicit request (switch / add / re-enter) wins over whatever is showing.
        var pending = _accounts.PendingAccount;
        var forceLogin = _accounts.PendingForceLogin;
        _accounts.PendingAccount = null;
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
                await LoadAccountAsync(account, forceLogin: false);
            else
                await Shell.Current.GoToAsync(SelectorRoute, animate: false);
        }
    }

    private async Task LoadAccountAsync(Account account, bool forceLogin)
    {
        var previousUrl = _url;
        var previousId = _activeAccountId;

        // Preserve the outgoing account's (possibly refreshed) session before we swap the jar.
        if (!string.IsNullOrEmpty(previousId) && previousId != account.Id && !string.IsNullOrEmpty(previousUrl))
        {
            try { await _session.CaptureAsync(previousId, previousUrl); } catch { /* best-effort */ }
        }

        // The SPA caches the signed-in user in localStorage (auth_user). Cookies alone aren't enough:
        // with stale cached identity the SPA boots into the portal, its API calls 401, and the web
        // app then auto-logs-out (which our bridge turns into a native logout). Wipe the current
        // page's web storage so the swapped-in session is the single source of truth.
        if (!string.IsNullOrEmpty(previousUrl))
            await ClearWebStorageAsync();

        var domain = account.Domain;
        _loadedDomain = domain;
        _url = AppConfig.TenantWebUrl(domain);
        _activeAccountId = account.Id;
        _loggingOut = false;
        // Commit a brand-new (forced-login) account to the saved list only once its portal loads.
        _commitAccount = forceLogin ? account : null;

        // Cover the WebView while the new org loads so the previous account's page never flashes,
        // showing this org's own logo (falls back to the LamaERP wordmark).
        OverlayLogo.Source = _branding.LogoOrDefault(domain);
        _loadingAccount = true;
        ShowLoadOverlay(true);

        // Safety net in case the "ready" signal is missed.
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(8), () => { if (_loadingAccount) EndAccountLoad(); });

#if WINDOWS
        AttachWindowsWebView();
#endif

        if (forceLogin)
            await _session.ClearAsync(account.Id, _url);     // empty the jar -> the login page shows
        else
            await _session.SwitchToAsync(account.Id, _url);  // load this account's saved session

        // Re-assigning the same URL won't re-navigate, so the already-rendered SPA would keep the
        // previous session on screen. When the origin is unchanged (e.g. adding/switching a user of
        // a tenant that's already shown), force a fresh bootstrap now that the jar has been swapped.
        if (string.Equals(previousUrl, _url, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(previousUrl))
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

        // Mirror the current (possibly just-issued) auth cookies into SecureStorage for this account.
        if (!string.IsNullOrEmpty(_activeAccountId))
        {
            try { await _session.CaptureAsync(_activeAccountId, _url); }
            catch { /* capture is best-effort */ }
        }
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

        var accountId = _activeAccountId;
        var url = _url;

        // Clear this account's session (cookie jar + SecureStorage) and forget the saved login.
        if (!string.IsNullOrEmpty(accountId) && !string.IsNullOrEmpty(url))
        {
            try { await _session.ClearAsync(accountId, url); } catch { /* best-effort */ }
        }
        if (!string.IsNullOrEmpty(accountId))
        {
            _profiles.RemoveAvatar(accountId);
            _accounts.Remove(accountId);
        }

        _accounts.ClearActive();
        _activeAccountId = null;
        _commitAccount = null;
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

    // Clears the currently-loaded page's localStorage/sessionStorage (the SPA's cached identity and
    // per-user caches) so a swapped-in account never inherits the previous user's web-side state.
    // Runs on the live document, whose origin matches the account we're (re)loading in the cases
    // that matter (same tenant, or a Debug build where all tenants share localhost).
    private async Task ClearWebStorageAsync()
    {
        try { await Web.EvaluateJavaScriptAsync("(function(){try{localStorage.clear();sessionStorage.clear();}catch(e){}return 1;})()"); }
        catch { /* best-effort */ }
    }

#if WINDOWS
    // Give the live WebView2 to the Windows session so it can read/write the cookie jar.
    private void AttachWindowsWebView()
    {
        if (_session is Platforms.Windows.WindowsWebSession ws &&
            Web.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 native)
        {
            ws.View = native;
        }
    }
#endif

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

        // Reaching the portal means the user is signed into this account.
        if (IsPortalPath(path) && !string.IsNullOrEmpty(_activeAccountId))
        {
            if (_commitAccount is { } commit && string.Equals(_activeAccountId, commit.Id, StringComparison.Ordinal))
            {
                // A newly-added account just signed in — finalize it (identity, de-dupe, save).
                _commitAccount = null;
                _ = FinalizeNewAccountAsync(commit);
            }
            else
            {
                _accounts.SetActive(_activeAccountId);
                _ = RefreshProfileAsync(_activeAccountId); // keep an existing account's name/photo fresh
            }
        }
    }

    // After a newly-added account reaches its portal: capture its session, ask the backend who
    // actually signed in (reliable — not guesswork), and either save it as a new account or — if that
    // same user of that tenant is already saved — keep the existing one and say it's already added.
    private async Task FinalizeNewAccountAsync(Account commit)
    {
        // Make sure the just-issued login cookies are persisted under this account first.
        try { await _session.CaptureAsync(commit.Id, _url); } catch { /* best-effort */ }

        var header = await _sessionStore.LoadAsync(commit.Id);
        var user = await _profiles.FetchAsync(commit.Domain, header);

        var existing = _accounts.FindByUser(commit.Domain, user?.UserId, excludingId: commit.Id);
        if (existing is not null)
        {
            // Same tenant + same user is already saved: don't create a duplicate. Move the fresh
            // session onto the existing account, drop the throwaway one, and select it.
            await _sessionStore.SaveAsync(existing.Id, header);
            _sessionStore.Remove(commit.Id);
            _activeAccountId = existing.Id;
            _accounts.SetActive(existing.Id);
            _ = _profiles.SaveAvatarAsync(existing.Id, existing.Domain, user?.ThumbUrl, header);

            await DisplayAlertAsync("Already added",
                $"{(string.IsNullOrWhiteSpace(existing.Email) ? existing.Name : existing.Email)} is already in your accounts.",
                "OK");
            return;
        }

        var finalized = commit with
        {
            UserKey = user?.UserId ?? string.Empty,
            Name = user?.Name ?? commit.Name,
            Email = user?.Email ?? user?.Username,
        };
        _accounts.AddOrUpdate(finalized);
        _accounts.SetActive(finalized.Id);
        await _profiles.SaveAvatarAsync(finalized.Id, finalized.Domain, user?.ThumbUrl, header);
    }

    // Refresh a saved account's display name / email / avatar from the backend (best-effort). Repairs
    // older entries and keeps the photo current; also backfills the user id if it was missed.
    private async Task RefreshProfileAsync(string accountId)
    {
        var account = _accounts.All.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;

        var header = await _sessionStore.LoadAsync(accountId);
        var user = await _profiles.FetchAsync(account.Domain, header);
        if (user is null) return;

        await _profiles.SaveAvatarAsync(accountId, account.Domain, user.ThumbUrl, header);

        var updated = account with
        {
            UserKey = string.IsNullOrEmpty(account.UserKey) ? user.UserId : account.UserKey,
            Name = user.Name,
            Email = user.Email ?? user.Username ?? account.Email,
        };
        if (updated != account)
            _accounts.AddOrUpdate(updated);
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
