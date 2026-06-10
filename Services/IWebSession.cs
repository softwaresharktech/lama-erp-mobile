namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Bridges the single native WebView cookie jar and <see cref="SessionStore"/> (SecureStorage).
/// Because all accounts of one tenant share an origin (and therefore one cookie jar), the active
/// account's session is swapped in/out of the jar on every switch. httpOnly cookies are invisible
/// to JavaScript but readable/writable from native code, which is how the JWTs move between the jar
/// and encrypted storage.
/// </summary>
public interface IWebSession
{
    /// <summary>Make the jar hold exactly this account's session for <paramref name="url"/>:
    /// clear the host's current cookies, then load this account's saved cookies (if any).
    /// Call BEFORE navigating so the SPA boots with the right session.</summary>
    Task SwitchToAsync(string accountId, string url);

    /// <summary>Pull the jar's current cookies for this host into SecureStorage under this account
    /// (after login / navigation), capturing freshly-issued tokens.</summary>
    Task CaptureAsync(string accountId, string url);

    /// <summary>Empty the jar for this host and forget this account's saved session — used for a
    /// fresh login (newly-added account) and on logout.</summary>
    Task ClearAsync(string accountId, string url);
}
