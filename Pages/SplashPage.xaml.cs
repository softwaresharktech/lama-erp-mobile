using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

public partial class SplashPage : ContentPage
{
    private readonly AccountStore _accounts;
    private readonly OrgBranding _branding;

    public SplashPage(AccountStore accounts, OrgBranding branding)
    {
        InitializeComponent();
        _accounts = accounts;
        _branding = branding;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // On launch, resume the selected account; if none is selected (e.g. after logout) but
        // accounts exist, auto-pick the most-recent one. Show THAT org's cached logo on the splash.
        var resume = _accounts.Active; // selected account, or most-recent fallback
        if (resume is not null)
        {
            var host = new Uri(AppConfig.TenantWebUrl(resume.Domain)).Host;
            if (_branding.HasLogo(host))
                Logo.Source = ImageSource.FromFile(_branding.LogoPathFor(host));
        }

        // Keep this short — it continues seamlessly into the web load overlay (same org logo,
        // same white background), so there is no separate lingering splash screen.
        await Task.Delay(300);

        if (resume is null)
        {
            await Shell.Current.GoToAsync("//identifier", animate: false);
            return;
        }

        // Resume that account (auto-login from its saved session).
        _accounts.PendingAccount = resume;
        _accounts.PendingForceLogin = false;
        await Shell.Current.GoToAsync("//web", animate: false);
    }
}
