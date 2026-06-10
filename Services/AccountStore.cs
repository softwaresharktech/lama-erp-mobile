using System.Text.Json;

namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Persists the list of organizations the user has signed into and which one is active, so the
/// app can switch between them. Stored in Preferences (non-sensitive metadata only — the tokens
/// live in <see cref="SessionStore"/>). Most-recently-used is kept first.
/// </summary>
public sealed class AccountStore
{
    private const string ListKey = "accounts";
    private const string ActiveKey = "active_domain";

    private List<Account>? _cache;

    /// <summary>Transient (not persisted) hint telling the WebView which org to load next —
    /// used when adding/re-entering an org so it loads that one rather than the active one.</summary>
    public string? PendingDomain { get; set; }

    /// <summary>Transient: when true, the pending load should start a FRESH login (clear that
    /// org's cookies first) instead of resuming a saved session. Set when the user explicitly
    /// enters an org on the identifier page, so "add another user of the same tenant" shows login.</summary>
    public bool PendingForceLogin { get; set; }

    /// <summary>Transient: an org the user is trying to sign into (entered on the identifier page).
    /// It is only committed to the saved list once login actually succeeds (the portal loads), so a
    /// cancelled sign-in never leaves a logged-out account behind.</summary>
    public Account? PendingAccount { get; set; }

    public IReadOnlyList<Account> All => Load();

    public bool HasAny => Load().Count > 0;

    public Account? Active
    {
        get
        {
            var list = Load();
            var domain = Preferences.Default.Get(ActiveKey, string.Empty);
            return list.FirstOrDefault(a => a.Domain == domain) ?? list.FirstOrDefault();
        }
    }

    /// <summary>True once the user has actually signed into an org (an active session exists).
    /// Distinguishes "adding another account" from the very first sign-in.</summary>
    public bool HasActiveSession
    {
        get
        {
            var domain = Preferences.Default.Get(ActiveKey, string.Empty);
            return !string.IsNullOrEmpty(domain) &&
                   Load().Any(a => string.Equals(a.Domain, domain, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The domain of the currently-selected account, or null if none is selected
    /// (e.g. right after logout). Used to decide whether to resume into the web app or show
    /// the account selector on launch.</summary>
    public string? ActiveDomain
    {
        get { var d = Preferences.Default.Get(ActiveKey, string.Empty); return string.IsNullOrEmpty(d) ? null : d; }
    }

    public void SetActive(string domain) => Preferences.Default.Set(ActiveKey, domain);

    /// <summary>Forget the current selection (after logout) so the next launch shows the selector.</summary>
    public void ClearActive() => Preferences.Default.Remove(ActiveKey);

    /// <summary>Adds a new organization (or moves an existing one to the front). Does NOT mark it
    /// active — an org only becomes active once the user actually reaches its portal (signed in).</summary>
    public void AddOrUpdate(Account account)
    {
        var list = Load();
        list.RemoveAll(a => string.Equals(a.Domain, account.Domain, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, account);
        Save(list);
    }

    public void Remove(string domain)
    {
        var list = Load();
        list.RemoveAll(a => string.Equals(a.Domain, domain, StringComparison.OrdinalIgnoreCase));
        Save(list);

        if (string.Equals(Preferences.Default.Get(ActiveKey, string.Empty), domain, StringComparison.OrdinalIgnoreCase))
        {
            if (list.Count > 0) SetActive(list[0].Domain);
            else Preferences.Default.Remove(ActiveKey);
        }
    }

    private List<Account> Load()
    {
        if (_cache is not null) return _cache;
        var json = Preferences.Default.Get(ListKey, string.Empty);
        _cache = string.IsNullOrEmpty(json)
            ? new List<Account>()
            : JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
        return _cache;
    }

    private void Save(List<Account> list)
    {
        _cache = list;
        Preferences.Default.Set(ListKey, JsonSerializer.Serialize(list));
    }
}
