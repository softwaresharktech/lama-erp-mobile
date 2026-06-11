namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// JavaScript injected into the tenant web app on every navigation. It does two things:
///  1. Tags every API call with <c>X-Client-Type: mobile</c> so the backend grants the longer
///     mobile refresh-token lifetime at login/refresh time.
///  2. Exposes <c>window.LamaMobile</c> so the web portal can show app-only actions (Switch
///     account) and hand control back to the native shell via the <c>lamaerpapp://</c> scheme.
/// </summary>
public static class WebBridge
{
    /// <summary>Custom scheme the WebView intercepts to run native commands from web JS.</summary>
    public const string Scheme = "lamaerpapp";
    public const string SwitchAccountUrl = Scheme + "://accounts";
    public const string LogoutUrl = Scheme + "://logout";

    public static readonly string InjectClientTypeJs = $$"""
        (function () {
            if (!window.LamaMobile) {
                window.LamaMobile = {
                    switchAccount: function () { window.location.href = '{{SwitchAccountUrl}}'; },
                    logout: function () { window.location.href = '{{LogoutUrl}}'; }
                };
            }
            try { window.dispatchEvent(new Event('lama-mobile-ready')); } catch (e) { }

            // Lock the viewport so the WebView feels like native chrome — no pinch / double-tap zoom.
            // Re-applied on every injection because the SPA may set its own viewport meta on boot.
            try {
                var vp = 'width=device-width, initial-scale=1, maximum-scale=1, minimum-scale=1, user-scalable=no, viewport-fit=cover';
                var meta = document.querySelector('meta[name=viewport]');
                if (!meta) { meta = document.createElement('meta'); meta.setAttribute('name', 'viewport'); (document.head || document.documentElement).appendChild(meta); }
                meta.setAttribute('content', vp);
            } catch (e) { }
            if (!window.__lamaNoZoom) {
                window.__lamaNoZoom = true;
                // iOS WKWebView honours these to block pinch / double-tap zoom regardless of viewport.
                try { document.addEventListener('gesturestart', function (e) { e.preventDefault(); }, { passive: false }); } catch (e) { }
                try { document.addEventListener('dblclick', function (e) { e.preventDefault(); }, { passive: false }); } catch (e) { }
            }

            // Android's System WebView exposes the Credential Management API (window.PasswordCredential)
            // but throws "user agent does not support public key credentials" when storing. The tenant
            // login calls navigator.credentials.store() after a successful sign-in; the throw aborts the
            // redirect to /portal. Neutralize it so login completes. (Saving passwords to the browser
            // is a no-op in a WebView anyway.)
            try { Object.defineProperty(window, 'PasswordCredential', { value: undefined, configurable: true }); }
            catch (e) { try { window.PasswordCredential = undefined; } catch (e2) { } }
            try {
                if (navigator.credentials && navigator.credentials.store) {
                    navigator.credentials.store = function () { return Promise.resolve(null); };
                }
            } catch (e) { }

            if (window.__lamaMobilePatched) return;
            window.__lamaMobilePatched = true;
            var HEADER = '{{AppConfig.ClientTypeHeader}}';
            var VALUE = '{{AppConfig.ClientTypeValue}}';

            // Only tag SAME-ORIGIN requests. Adding a custom header to a cross-origin request
            // (e.g. a 3rd-party integration "test connection") triggers a CORS preflight the
            // remote server rejects — which broke those calls inside the WebView.
            function isApi(url) {
                try {
                    if (typeof url !== 'string') return false;
                    if (url.indexOf('//') === 0) return false;            // protocol-relative, cross-origin
                    if (url.charAt(0) === '/') return true;               // root-relative, same origin
                    if (url.indexOf(location.origin) === 0) return true;  // absolute, same origin
                    if (url.indexOf('://') === -1) return true;           // relative path, same origin
                    return false;                                          // absolute cross-origin
                } catch (e) { return false; }
            }

            var origFetch = window.fetch;
            if (origFetch) {
                window.fetch = function (input, init) {
                    var url = '';
                    try {
                        url = (typeof input === 'string') ? input : (input && input.url) || '';
                        if (isApi(url)) {
                            init = init || {};
                            var h = new Headers((init && init.headers) || (typeof input !== 'string' && input.headers) || {});
                            h.set(HEADER, VALUE);
                            init.headers = h;
                            if (!init.credentials) init.credentials = 'include';
                        }
                    } catch (e) { }
                    // Explicit logout: the web calls /api/auth/logout. Hand off to the native account
                    // selector (works with no web change; only logout hits this, not session expiry).
                    try {
                        if (url && url.indexOf('/api/auth/logout') !== -1) {
                            setTimeout(function () { window.location.href = '{{LogoutUrl}}'; }, 0);
                        }
                    } catch (e) { }
                    return origFetch.call(this, input, init);
                };
            }

            var origOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function (method, url) {
                this.__lamaIsApi = isApi(url);
                return origOpen.apply(this, arguments);
            };
            var origSend = XMLHttpRequest.prototype.send;
            XMLHttpRequest.prototype.send = function (body) {
                try { if (this.__lamaIsApi) this.setRequestHeader(HEADER, VALUE); } catch (e) { }
                return origSend.apply(this, arguments);
            };

            // Browser-style pull-to-refresh on the app's real scroll container. Touch only,
            // so it never interferes with taps or desktop pointers.
            (function () {
                var THRESHOLD = 80;
                var startY = null, scroller = null;
                function findScroller() {
                    return document.querySelector('main') || document.scrollingElement || document.documentElement;
                }
                document.addEventListener('touchstart', function (e) {
                    if (e.touches.length !== 1) { startY = null; return; }
                    scroller = findScroller();
                    startY = (scroller && scroller.scrollTop <= 0) ? e.touches[0].clientY : null;
                }, { passive: true });
                document.addEventListener('touchend', function (e) {
                    if (startY === null) return;
                    var dy = e.changedTouches[0].clientY - startY;
                    if (scroller && scroller.scrollTop <= 0 && dy > THRESHOLD) window.location.reload();
                    startY = null;
                }, { passive: true });
            })();
        })();
        """;
}
