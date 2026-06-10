using LamaERP.Mobile.WebApp.Services;

namespace LamaERP.Mobile.WebApp.Platforms.Windows;

/// <summary>
/// Windows (WebView2) cookie bridge. WebView2 persists its own cookie jar in the per-app user
/// data folder across runs, so restore/capture are no-ops here — this target exists mainly for
/// quick desktop dev runs. Clearing still drops the SecureStorage backup record.
/// </summary>
public sealed class WindowsWebSession : IWebSession
{
    private readonly SessionStore _store;

    public WindowsWebSession(SessionStore store) => _store = store;

    public Task RestoreAsync(string url) => Task.CompletedTask;

    public Task CaptureAsync(string url) => Task.CompletedTask;

    public Task ClearAsync(string url)
    {
        _store.Remove(new Uri(url).Host);
        return Task.CompletedTask;
    }
}
