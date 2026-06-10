using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

public partial class IdentifierPage : ContentPage
{
    private readonly TenantResolver _resolver;
    private readonly AccountStore _accounts;
    private readonly OrgBranding _branding;

    public IdentifierPage(TenantResolver resolver, AccountStore accounts, OrgBranding branding)
    {
        InitializeComponent();
        _resolver = resolver;
        _accounts = accounts;
        _branding = branding;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Show the top bar (with Cancel) only when there are other saved logins to go back to.
        // On the very first sign-in there's nowhere to return to, so no bar at all.
        TopBar.IsVisible = _accounts.HasAny;
        ErrorLabel.IsVisible = false;
        ResetBorder();
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        ResetBorder();

        var domain = NormalizeDomain(DomainEntry.Text);
        if (string.IsNullOrEmpty(domain))
        {
            ShowError("Enter your organization address to continue.");
            return;
        }

        // Guard against malformed hostnames before they reach the URI builder (which would throw).
        if (Uri.CheckHostName(domain) == UriHostNameType.Unknown)
        {
            ShowError("That doesn't look like a valid organization address. Check it and try again.");
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _resolver.ResolveAsync(domain);
            if (!result.Success)
            {
                ShowError(result.Error ?? "Couldn't verify that organization. Try again.");
                return;
            }

            // Remember the org to sign into, but DON'T save it yet — it's only committed to the
            // account list once login actually succeeds (so a cancelled sign-in leaves nothing behind).
            _accounts.PendingAccount = new Account(domain, result.Identifier!, string.IsNullOrWhiteSpace(result.Name) ? domain : result.Name!);

            // Cache this org's logo now (from the resolve response) so the loading cover shows it.
            await _branding.SaveLogoAsync(domain, result.LogoUrl);

            // Load this org next, forcing a fresh login (so adding another user of the same
            // tenant shows the login page instead of resuming the existing session).
            _accounts.PendingDomain = domain;
            _accounts.PendingForceLogin = true;
            await Shell.Current.GoToAsync("//web", animate: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//accounts", animate: false);

    // Accepts "https://northgate.lamaerp.com/", "northgate.lamaerp.com" or a bare "northgate"
    // (assumed to live on lamaerp.com) and returns the host to resolve.
    private static string NormalizeDomain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var value = raw.Trim().ToLowerInvariant();
        // Strip any internal whitespace (mobile keyboards often inject stray spaces).
        value = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (value.StartsWith("http://")) value = value[7..];
        else if (value.StartsWith("https://")) value = value[8..];

        var slash = value.IndexOfAny(['/', '?', '#']);
        if (slash >= 0) value = value[..slash];
        value = value.Trim('.');

        if (value.Length == 0) return string.Empty;
        return value.Contains('.') ? value : $"{value}.lamaerp.com";
    }

    private void SetBusy(bool busy)
    {
        ContinueButton.IsEnabled = !busy;
        ContinueButton.Text = busy ? "Checking…" : "Continue";
        DomainEntry.IsEnabled = !busy;
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
        DomainBorder.Stroke = new SolidColorBrush(Brand("Danger", Colors.Crimson));
    }

    private void ResetBorder()
        => DomainBorder.Stroke = new SolidColorBrush(Brand("Border", Colors.LightGray));

    private static Color Brand(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;
}
