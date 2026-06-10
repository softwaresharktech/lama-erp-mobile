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
        var resumeDomain = _accounts.ActiveDomain ?? _accounts.All.FirstOrDefault()?.Domain;
        if (resumeDomain is not null)
        {
            var host = new Uri(AppConfig.TenantWebUrl(resumeDomain)).Host;
            if (_branding.HasLogo(host))
                Logo.Source = ImageSource.FromFile(_branding.LogoPathFor(host));
        }

        // Keep this short — it continues seamlessly into the web load overlay (same org logo,
        // same white background), so there is no separate lingering splash screen.
        await Task.Delay(300);

        if (resumeDomain is null)
        {
            await Shell.Current.GoToAsync("//identifier", animate: false);
            return;
        }

        // If nothing was selected, auto-select the most-recent account so it resumes (auto-login).
        if (_accounts.ActiveDomain is null)
            _accounts.PendingDomain = resumeDomain;

        await Shell.Current.GoToAsync("//web", animate: false);
    }
}
