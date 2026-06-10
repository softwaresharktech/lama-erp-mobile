namespace LamaERP.Mobile.WebApp.Services;

/// <summary>Helpers for the auth cookie header ("name=value; name2=value2").</summary>
public static class AuthCookies
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";

    /// <summary>True when the header actually carries a session (an access or refresh token).
    /// Used to avoid persisting an empty/logged-out jar — e.g. a navigation to the login page must
    /// NOT overwrite a still-valid saved session with nothing.</summary>
    public static bool HasSession(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        foreach (var pair in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = pair.Split('=', 2)[0];
            if (name.Equals(AccessToken, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(RefreshToken, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
