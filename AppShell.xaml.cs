using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // The splash page (the first route) decides where to go, so there is no
        // flash of the identifier page on relaunch.
    }
}
