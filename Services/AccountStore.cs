using System.Text.Json;

namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// Persists the list of sign-ins the user can switch between and which one is active. Accounts are
/// identified by their stable <see cref="Account.Id"/>, so several users of the same tenant coexist.
/// Stored in Preferences (non-sensitive metadata only — the tokens live in <see cref="SessionStore"/>).
/// Most-recently-used is kept first.
/// </summary>
public sealed class AccountStore
{
    // v2: accounts are keyed by a stable id (was: by tenant/host). The bump discards legacy entries
    // saved under the old shape instead of deserializing them into null-id accounts.
    private const string ListKey = "accounts_v2";
    private const string ActiveKey = "active_account_id";

    private List<Account>? _cache;

    /// <summary>Transient (not persisted) hint telling the WebView which account to load next — an
    /// existing one (switch/resume) or a brand-new one being added.</summary>
    public Account? PendingAccount { get; set; }

    /// <summary>Transient: when true, the pending load should start a FRESH login (empty the jar)
    /// instead of resuming a saved session. Set when the user adds a new account on the identifier
    /// page, so "add another user of the same tenant" shows the login screen.</summary>
    public bool PendingForceLogin { get; set; }

    public IReadOnlyList<Account> All => Load();

    public bool HasAny => Load().Count > 0;

    /// <summary>The id of the currently-selected account, or null if none is selected (e.g. right
    /// after logout).</summary>
    public string? ActiveId
    {
        get { var v = Preferences.Default.Get(ActiveKey, string.Empty); return string.IsNullOrEmpty(v) ? null : v; }
    }

    /// <summary>The currently-selected account, falling back to the most-recent one when nothing is
    /// explicitly selected.</summary>
    public Account? Active
    {
        get
        {
            var list = Load();
            var id = ActiveId;
            return list.FirstOrDefault(a => a.Id == id) ?? list.FirstOrDefault();
        }
    }

    /// <summary>The domain of the explicitly-selected account, or null if none is selected.</summary>
    public string? ActiveDomain
    {
        get { var id = ActiveId; return id is null ? null : Load().FirstOrDefault(a => a.Id == id)?.Domain; }
    }

    /// <summary>True once the user has actually signed into an account (an active selection exists).
    /// Distinguishes "adding another account" from the very first sign-in.</summary>
    public bool HasActiveSession => ActiveId is { } id && Load().Any(a => a.Id == id);

    public void SetActive(string id) => Preferences.Default.Set(ActiveKey, id);

    /// <summary>Forget the current selection (after logout) so the next launch shows the selector.</summary>
    public void ClearActive() => Preferences.Default.Remove(ActiveKey);

    /// <summary>Adds an account (or moves an existing one — same id — to the front). Does NOT mark it
    /// active; an account only becomes active once the user actually reaches its portal.</summary>
    public void AddOrUpdate(Account account)
    {
        var list = Load();
        list.RemoveAll(a => a.Id == account.Id);
        list.Insert(0, account);
        Save(list);
    }

    public void Remove(string id)
    {
        var list = Load();
        list.RemoveAll(a => a.Id == id);
        Save(list);

        if (string.Equals(ActiveId, id, StringComparison.Ordinal))
        {
            if (list.Count > 0) SetActive(list[0].Id);
            else Preferences.Default.Remove(ActiveKey);
        }
    }

    /// <summary>Finds an already-saved account for the same tenant + user (so a repeat sign-in isn't
    /// stored twice), ignoring the account currently being added.</summary>
    public Account? FindByUser(string domain, string? userKey, string? excludingId = null)
    {
        if (string.IsNullOrEmpty(userKey)) return null;
        return Load().FirstOrDefault(a =>
            a.Id != excludingId &&
            string.Equals(a.Domain, domain, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.UserKey, userKey, StringComparison.OrdinalIgnoreCase));
    }

    private List<Account> Load()
    {
        if (_cache is not null) return _cache;
        var json = Preferences.Default.Get(ListKey, string.Empty);
        try
        {
            _cache = string.IsNullOrEmpty(json)
                ? new List<Account>()
                : (JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>())
                    .Where(a => !string.IsNullOrEmpty(a.Id))
                    .ToList();
        }
        catch
        {
            // Shape changed (older builds keyed accounts differently) — start clean rather than crash.
            _cache = new List<Account>();
        }
        return _cache;
    }

    private void Save(List<Account> list)
    {
        _cache = list;
        Preferences.Default.Set(ListKey, JsonSerializer.Serialize(list));
    }
}
