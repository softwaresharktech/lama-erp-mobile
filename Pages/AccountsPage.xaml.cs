using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

/// <summary>A saved account decorated for display (active marker, avatar, secondary line).</summary>
public sealed record AccountItem(Account Account, bool IsActive, ImageSource? Avatar)
{
    public string Initial => string.IsNullOrEmpty(Account.Name) ? "?" : Account.Name[0..1].ToUpperInvariant();

    public bool HasAvatar => Avatar is not null;
    public bool ShowInitial => Avatar is null;

    /// <summary>Second line: the signed-in user's email/username and the tenant name, so two users of
    /// the same tenant are distinguishable.</summary>
    public string Subtitle
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(Account.Email) ? null : Account.Email;
            var org = string.IsNullOrWhiteSpace(Account.TenantName) ? Account.Domain : Account.TenantName;
            return string.IsNullOrEmpty(who) ? org! : $"{who} · {org}";
        }
    }
}

public partial class AccountsPage : ContentPage
{
    private readonly AccountStore _accounts;
    private readonly UserProfileService _profiles;

    public AccountsPage(AccountStore accounts, UserProfileService profiles)
    {
        InitializeComponent();
        _accounts = accounts;
        _profiles = profiles;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Reload();

        // Cancel is shown only when there's a current account to return to. After logout
        // (or a fresh launch with no selection) there is none, so the user must pick/add.
        CancelButton.IsVisible = _accounts.HasActiveSession;

        // Slide up from the bottom.
        var height = Height > 0 ? Height : 800;
        Sheet.TranslationY = height;
        await Sheet.TranslateToAsync(0, 0, 280, Easing.CubicOut);
    }

    private void Reload()
    {
        var activeId = _accounts.ActiveId;
        AccountsList.ItemsSource = _accounts.All
            .Select(a => new AccountItem(a, string.Equals(a.Id, activeId, StringComparison.Ordinal), _profiles.AvatarOrNull(a.Id)))
            .ToList();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//web", animate: false);

    private async void OnAddClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//identifier", animate: false);

    private async void OnAccountTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: AccountItem item })
        {
            _accounts.PendingAccount = item.Account;
            _accounts.PendingForceLogin = false; // resume this account's saved session
            await Shell.Current.GoToAsync("//web", animate: false);
        }
    }
}
