using Microsoft.Extensions.DependencyInjection;

namespace LamaERP.Mobile.WebApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolve the shell through DI so its dependencies (AccountStore) are injected.
        var services = IPlatformApplication.Current!.Services;
        var shell = services.GetRequiredService<AppShell>();
        // On Windows the phone-sized window is enforced via the lifecycle event in MauiProgram
        // (Window.Width is unreliable on WinUI).
        return new Window(shell) { Title = "LamaERP" };
    }
}
