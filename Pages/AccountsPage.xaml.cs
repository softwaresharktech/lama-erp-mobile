using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Pages;

/// <summary>A saved account decorated for display (active marker + avatar initial).</summary>
public sealed record AccountItem(Account Account, bool IsActive)
{
    public string Initial => string.IsNullOrEmpty(Account.Name) ? "?" : Account.Name[0..1].ToUpperInvariant();
}

public partial class AccountsPage : ContentPage
{
    private readonly AccountStore _accounts;

    public AccountsPage(AccountStore accounts)
    {
        InitializeComponent();
        _accounts = accounts;
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
        var activeDomain = _accounts.ActiveDomain;
        AccountsList.ItemsSource = _accounts.All
            .Select(a => new AccountItem(a, string.Equals(a.Domain, activeDomain, StringComparison.OrdinalIgnoreCase)))
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
            _accounts.PendingDomain = item.Account.Domain;
            await Shell.Current.GoToAsync("//web", animate: false);
        }
    }
}
