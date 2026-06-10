namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Resolves the origin used both for the tenant-resolve API call and for the WebView.
///
/// RELEASE → each organization is served at https://{domain}; the gateway behind that host
///           also routes /api/* and serves the Vue tenant SPA, so a single origin covers
///           resolve + login + the whole web flow.
/// DEBUG   → a fixed local gateway on your LAN, so you can test against a stack running on
///           your PC. The entered domain is still sent as X-Tenant-Id for resolution.
/// </summary>
public static class AppConfig
{
    // ⚠️ LOCAL TESTING: replace with your PC's LAN IPv4 (run `ipconfig`, use the Wi-Fi IPv4,
    // e.g. 192.168.1.42). The device must be on the SAME network and the stack must listen on
    // all interfaces. NOTE: the auth cookies are marked Secure, so they are only stored over
    // HTTPS — for a faithful login test, point this at an https dev origin.
    public const string LocalOrigin = "http://192.168.1.42:5001";

    /// <summary>Marks every request from this app so the backend grants the longer
    /// mobile refresh-token lifetime (see ClientType.Mobile / X-Client-Type).</summary>
    public const string ClientTypeHeader = "X-Client-Type";
    public const string ClientTypeValue = "mobile";

    public static string OriginFor(string domain)
    {
#if DEBUG
        return LocalOrigin;
#else
        return $"https://{domain}";
#endif
    }

    public static string ResolveApiUrl(string domain) => $"{OriginFor(domain)}/api/auth/tenant/resolve";

    /// <summary>The tenant web app entry point loaded in the WebView.</summary>
    public static string TenantWebUrl(string domain) => OriginFor(domain) + "/";
}
