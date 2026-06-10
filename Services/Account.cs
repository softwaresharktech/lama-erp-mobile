namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// A saved sign-in the user can switch between. Identity is the stable <see cref="Id"/> (a GUID
/// minted when the sign-in is started), NOT the tenant — so two different users of the SAME tenant
/// are distinct accounts that coexist. <see cref="UserKey"/> (the backend user id) identifies the
/// logged-in user within a tenant and is filled in once login succeeds; it's used to detect "this
/// account is already added".
///
/// <see cref="Name"/> is the signed-in user's display name (the tenant name is used only as a
/// placeholder until login resolves the real user). <see cref="Email"/> and <see cref="TenantName"/>
/// are shown in the account picker so two users of one tenant are distinguishable. The session (JWT
/// cookies) is held per-<see cref="Id"/> in SecureStorage via <see cref="SessionStore"/>.
/// </summary>
public sealed record Account(
    string Id,
    string Domain,
    string UserKey,
    string Name,
    string? Email = null,
    string? TenantName = null)
{
    public static Account New(string domain, string tenantName) =>
        new(Guid.NewGuid().ToString("N"), domain, string.Empty, tenantName, Email: null, TenantName: tenantName);
}
