namespace LamaERP.Mobile.WebApp.Services;

/// <summary>
/// JavaScript that bridges the web app's WebAuthn calls to native passkeys on iOS.
///
/// WKWebView does not perform passkey (WebAuthn) operations on behalf of embedded web content —
/// <c>navigator.credentials.create()/get()</c> reject with NotAllowedError. This shim replaces those
/// two methods: it converts the request's ArrayBuffer fields to base64url JSON, hands them to the
/// native <c>lamaPasskey</c> message handler (which runs ASAuthorizationController), and resolves the
/// original Promise with a PublicKeyCredential-shaped object once native returns the result.
///
/// The interception is at the <c>navigator.credentials</c> level, so it is independent of the web's
/// WebAuthn helper library (the tenant app uses @github/webauthn-json, which calls these methods and
/// then serializes the result — so the object we return must expose the same fields/getters a real
/// PublicKeyCredential does).
/// </summary>
public static class PasskeyBridge
{
    /// <summary>Name of the WKScriptMessageHandler the native side registers.</summary>
    public const string MessageHandlerName = "lamaPasskey";

    public static readonly string ShimJs = """
        (function () {
            if (window.__lamaPasskeyShim) return;
            var mh = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.lamaPasskey;
            if (!mh || !navigator.credentials) return;
            window.__lamaPasskeyShim = true;

            var pending = {};
            var seq = 0;

            function b64urlFromBuf(buf) {
                var bytes = new Uint8Array(buf), bin = '';
                for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
                return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
            }
            function bufFromB64url(s) {
                s = String(s).replace(/-/g, '+').replace(/_/g, '/');
                while (s.length % 4) s += '=';
                var bin = atob(s), bytes = new Uint8Array(bin.length);
                for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
                return bytes.buffer;
            }
            function descList(list) {
                return (list || []).map(function (c) {
                    return { type: c.type, id: b64urlFromBuf(c.id), transports: c.transports };
                });
            }

            // Serialize a CredentialCreationOptions.publicKey (ArrayBuffers -> base64url) for native.
            function encodeCreate(pk) {
                return {
                    challenge: b64urlFromBuf(pk.challenge),
                    rp: pk.rp,
                    user: { id: b64urlFromBuf(pk.user.id), name: pk.user.name, displayName: pk.user.displayName },
                    pubKeyCredParams: pk.pubKeyCredParams,
                    timeout: pk.timeout,
                    attestation: pk.attestation,
                    authenticatorSelection: pk.authenticatorSelection,
                    excludeCredentials: descList(pk.excludeCredentials)
                };
            }
            function encodeGet(pk) {
                return {
                    challenge: b64urlFromBuf(pk.challenge),
                    rpId: pk.rpId,
                    timeout: pk.timeout,
                    userVerification: pk.userVerification,
                    allowCredentials: descList(pk.allowCredentials)
                };
            }

            // Rebuild a PublicKeyCredential-shaped object from native's base64url JSON result.
            function decodeCredential(c) {
                var r = c.response || {};
                var response = c.kind === 'create'
                    ? {
                        clientDataJSON: bufFromB64url(r.clientDataJSON),
                        attestationObject: bufFromB64url(r.attestationObject),
                        getTransports: function () { return r.transports || ['internal']; },
                        getAuthenticatorData: function () { return r.authenticatorData ? bufFromB64url(r.authenticatorData) : null; },
                        getPublicKey: function () { return null; },
                        getPublicKeyAlgorithm: function () { return -7; }
                    }
                    : {
                        clientDataJSON: bufFromB64url(r.clientDataJSON),
                        authenticatorData: bufFromB64url(r.authenticatorData),
                        signature: bufFromB64url(r.signature),
                        userHandle: r.userHandle ? bufFromB64url(r.userHandle) : null
                    };
                return {
                    id: c.id,
                    rawId: bufFromB64url(c.rawId),
                    type: 'public-key',
                    authenticatorAttachment: c.authenticatorAttachment || 'platform',
                    response: response,
                    getClientExtensionResults: function () { return c.clientExtensionResults || {}; }
                };
            }

            function send(action, kind, options) {
                return new Promise(function (resolve, reject) {
                    var id = 'pk' + (++seq);
                    pending[id] = { resolve: resolve, reject: reject, kind: kind };
                    try { mh.postMessage({ id: id, action: action, optionsJson: JSON.stringify(options) }); }
                    catch (e) { delete pending[id]; reject(new DOMException('Passkey bridge unavailable', 'NotAllowedError')); }
                });
            }

            // Native -> JS resolution callbacks.
            window.__lamaPasskeyResolve = function (id, resultJson) {
                var p = pending[id]; if (!p) return; delete pending[id];
                try {
                    var c = JSON.parse(resultJson); c.kind = p.kind;
                    p.resolve(decodeCredential(c));
                } catch (e) { p.reject(new DOMException('Bad passkey result', 'NotAllowedError')); }
            };
            window.__lamaPasskeyReject = function (id, name, message) {
                var p = pending[id]; if (!p) return; delete pending[id];
                p.reject(new DOMException(message || 'Passkey operation failed', name || 'NotAllowedError'));
            };

            var origCreate = navigator.credentials.create ? navigator.credentials.create.bind(navigator.credentials) : null;
            var origGet = navigator.credentials.get ? navigator.credentials.get.bind(navigator.credentials) : null;

            navigator.credentials.create = function (options) {
                if (!options || !options.publicKey) return origCreate ? origCreate(options) : Promise.reject(new DOMException('Not supported', 'NotSupportedError'));
                return send('create', 'create', encodeCreate(options.publicKey));
            };
            navigator.credentials.get = function (options) {
                if (!options || !options.publicKey) return origGet ? origGet(options) : Promise.reject(new DOMException('Not supported', 'NotSupportedError'));
                // Conditional mediation (autofill) isn't supported by this native bridge yet — fall back
                // to the explicit modal flow rather than hanging on a background autofill request.
                if (options.mediation === 'conditional') return Promise.reject(new DOMException('Conditional UI unavailable', 'NotSupportedError'));
                return send('get', 'get', encodeGet(options.publicKey));
            };

            // Make sure the web's capability checks light up: PublicKeyCredential must be defined, and
            // conditional-mediation must report false so the page uses the explicit "passkey" button.
            try {
                if (typeof window.PublicKeyCredential === 'undefined') window.PublicKeyCredential = function () {};
                window.PublicKeyCredential.isConditionalMediationAvailable = function () { return Promise.resolve(false); };
                window.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable = function () { return Promise.resolve(true); };
            } catch (e) { }
        })();
        """;
}
