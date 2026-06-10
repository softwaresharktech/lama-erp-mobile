using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using LamaERP.Mobile.WebApp.Pages;
using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("PlusJakartaSans.ttf", "JakartaSans");
            });

        ConfigureWebView();

#if WINDOWS
        // This is a mobile app; on the Windows desktop force a phone-sized window so the web's
        // responsive mobile layout shows (Window.Width alone is unreliable on WinUI).
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(w => w.OnWindowCreated(window =>
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
                if (appWindow is null) return;

                var dpi = GetDpiForWindow(hwnd);
                var scale = dpi == 0 ? 1.0 : dpi / 96.0;
                appWindow.Resize(new Windows.Graphics.SizeInt32((int)(430 * scale), (int)(880 * scale)));
            }));
        });
#endif

        // Core services
        builder.Services.AddSingleton<AccountStore>();
        builder.Services.AddSingleton<SessionStore>();
        builder.Services.AddSingleton<TenantResolver>();
        builder.Services.AddSingleton<OrgBranding>();

        // Platform cookie bridge (JWT cookies <-> SecureStorage)
#if ANDROID
        builder.Services.AddSingleton<IWebSession, Platforms.Android.AndroidWebSession>();
#elif IOS
        builder.Services.AddSingleton<IWebSession, Platforms.iOS.IosWebSession>();
#elif WINDOWS
        builder.Services.AddSingleton<IWebSession, Platforms.Windows.WindowsWebSession>();
#endif

        // Shell + pages
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<SplashPage>();
        builder.Services.AddTransient<IdentifierPage>();
        builder.Services.AddTransient<AccountsPage>();
        builder.Services.AddTransient<WebAppPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void ConfigureWebView()
    {
#if ANDROID
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("LamaWebSettings", (handler, view) =>
        {
            var settings = handler.PlatformView.Settings;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.DatabaseEnabled = true;
            settings.SetGeolocationEnabled(true); // allow navigator.geolocation (staff check-in/out)

            // Approve the WebView's geolocation prompts (and keep MAUI's other chrome behaviour).
            handler.PlatformView.SetWebChromeClient(new Platforms.Android.GeoWebChromeClient(handler));

            var cookies = Android.Webkit.CookieManager.Instance;
            cookies?.SetAcceptCookie(true);
            cookies?.SetAcceptThirdPartyCookies(handler.PlatformView, true);
        });
#elif WINDOWS
        // EvaluateJavaScriptAsync is unreliable on WebView2; inject the bridge at document-start
        // via WebView2's own API so window.LamaMobile (Switch account) and the API header are set
        // before the SPA loads.
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("LamaWinInject", (handler, view) =>
        {
            var webview2 = handler.PlatformView;

            async void Inject()
            {
                try { await webview2.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WebBridge.InjectClientTypeJs); }
                catch { /* best-effort */ }
            }

            if (webview2.CoreWebView2 is not null) Inject();
            else webview2.CoreWebView2Initialized += (_, _) => Inject();
        });
#endif
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
#endif
}
