namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Bridges the native WebView cookie jar and <see cref="SessionStore"/> (SecureStorage).
/// httpOnly cookies are invisible to JavaScript but readable/writable from native code, which
/// is how the JWTs are mirrored into encrypted storage and back.
/// </summary>
public interface IWebSession
{
    /// <summary>Push the saved cookies from SecureStorage into the WebView jar (before loading).</summary>
    Task RestoreAsync(string url);

    /// <summary>Pull the current WebView cookies into SecureStorage (after navigation).</summary>
    Task CaptureAsync(string url);

    /// <summary>Forget the session in both the jar and SecureStorage (switch organization).</summary>
    Task ClearAsync(string url);
}
