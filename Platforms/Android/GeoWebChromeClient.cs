using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace LamaERP.Mobile.WebApp.Platforms.Android;

/// <summary>
/// Extends MAUI's WebChromeClient to approve the WebView's geolocation requests (so the tenant
/// web app's <c>navigator.geolocation</c> works for staff check-in/out). First ensures the app
/// itself holds the OS location permission, then grants the web origin. Subclassing the MAUI
/// client keeps its other behaviour (file pickers, JS dialogs) intact.
/// </summary>
public class GeoWebChromeClient : MauiWebChromeClient
{
    public GeoWebChromeClient(IWebViewHandler handler) : base(handler)
    {
    }

    public override async void OnGeolocationPermissionsShowPrompt(string? origin, GeolocationPermissions.ICallback? callback)
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            callback?.Invoke(origin, status == PermissionStatus.Granted, false);
        }
        catch
        {
            callback?.Invoke(origin, false, false);
        }
    }
}
