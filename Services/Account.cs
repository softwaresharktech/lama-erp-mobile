namespace LamaERP.Mobile.WebApp.Services;

/// <summary>A saved organization the user can switch between. The session (JWT cookies) is held
/// per-host in SecureStorage via <see cref="SessionStore"/>; this record is just the metadata.</summary>
public sealed record Account(string Domain, string Identifier, string Name);
