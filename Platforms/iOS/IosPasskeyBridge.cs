using System.Text;
using System.Text.Json;
using AuthenticationServices;
using Foundation;
using LamaERP.Mobile.WebApp.Services;
using UIKit;
using WebKit;

namespace LamaERP.Mobile.WebApp.Platforms.iOS;

/// <summary>
/// Native side of the iOS passkey bridge. Receives WebAuthn requests from <see cref="PasskeyBridge"/>'s
/// JS shim over the <c>lamaPasskey</c> message channel, runs the real passkey ceremony with
/// <see cref="ASAuthorizationController"/> (Face ID / Touch ID + iCloud Keychain), and resolves the
/// web Promise with the assertion/attestation marshalled back into WebAuthn-JSON shape.
///
/// The Relying Party ID is read per-request from <c>publicKey.rp.id</c> (login) / <c>publicKey.rpId</c>
/// (assertion), matching the backend's per-host RP model. For iOS to honour it, the app's Associated
/// Domains entitlement must cover that host (e.g. <c>webcredentials:*.lamaerp.com</c>) AND that host
/// must serve <c>/.well-known/apple-app-site-association</c> listing this app.
///
/// Platform passkeys require iOS 16; on earlier versions the shim's calls simply reject.
/// </summary>
public sealed class IosPasskeyBridge : NSObject, IWKScriptMessageHandler,
    IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
{
    private readonly WeakReference<WKWebView> _web;
    private string? _pendingId;
    private string _pendingKind = "get";

    public IosPasskeyBridge(WKWebView web) => _web = new WeakReference<WKWebView>(web);

    // ---- JS -> native ------------------------------------------------------------------------

    public void DidReceiveScriptMessage(WKUserContentController controller, WKScriptMessage message)
    {
        string? id = null;
        try
        {
            if (message.Body is not NSDictionary dict) return;
            id = (dict["id"] as NSString)?.ToString();
            var action = (dict["action"] as NSString)?.ToString();
            var optionsJson = (dict["optionsJson"] as NSString)?.ToString();
            if (id is null || action is null || optionsJson is null) return;

            if (!OperatingSystem.IsIOSVersionAtLeast(16))
            {
                Reject(id, "NotSupportedError", "Passkeys require iOS 16 or later.");
                return;
            }

            _pendingId = id;
            _pendingKind = action == "create" ? "create" : "get";

            using var doc = JsonDocument.Parse(optionsJson);
            var pk = doc.RootElement;
            if (action == "create") StartRegistration(pk);
            else StartAssertion(pk);
        }
        catch (Exception ex)
        {
            Reject(id ?? _pendingId, "NotAllowedError", ex.Message);
        }
    }

    private void StartRegistration(JsonElement pk)
    {
        var rpId = pk.GetProperty("rp").GetProperty("id").GetString() ?? string.Empty;
        var challenge = B64Url.Decode(pk.GetProperty("challenge").GetString()!);
        var user = pk.GetProperty("user");
        var userId = B64Url.Decode(user.GetProperty("id").GetString()!);
        var userName = user.GetProperty("name").GetString() ?? string.Empty;

        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(rpId);
        var request = provider.CreateCredentialRegistrationRequest(
            NSData.FromArray(challenge), userName, NSData.FromArray(userId));

        if (TryGetUserVerification(pk, out var uv)) request.UserVerificationPreference = uv;

        Run(request);
    }

    private void StartAssertion(JsonElement pk)
    {
        var rpId = pk.TryGetProperty("rpId", out var r) ? r.GetString() ?? string.Empty : string.Empty;
        var challenge = B64Url.Decode(pk.GetProperty("challenge").GetString()!);

        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(rpId);
        var request = provider.CreateCredentialAssertionRequest(NSData.FromArray(challenge));

        if (pk.TryGetProperty("allowCredentials", out var allow) && allow.ValueKind == JsonValueKind.Array && allow.GetArrayLength() > 0)
        {
            var descriptors = new List<ASAuthorizationPlatformPublicKeyCredentialDescriptor>();
            foreach (var c in allow.EnumerateArray())
            {
                var cid = B64Url.Decode(c.GetProperty("id").GetString()!);
                descriptors.Add(new ASAuthorizationPlatformPublicKeyCredentialDescriptor(NSData.FromArray(cid)));
            }
            request.AllowedCredentials = descriptors.ToArray();
        }

        if (TryGetUserVerification(pk, out var uv)) request.UserVerificationPreference = uv;

        Run(request);
    }

    private void Run(ASAuthorizationRequest request)
    {
        var controller = new ASAuthorizationController(new[] { request })
        {
            Delegate = this,
            PresentationContextProvider = this,
        };
        controller.PerformRequests();
    }

    // ---- native -> JS ------------------------------------------------------------------------

    [Export("authorizationController:didCompleteWithAuthorization:")]
    public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
    {
        var id = _pendingId;
        try
        {
            switch (authorization.Credential)
            {
                case ASAuthorizationPlatformPublicKeyCredentialRegistration reg:
                    ResolveRegistration(id, reg);
                    break;
                case ASAuthorizationPlatformPublicKeyCredentialAssertion asr:
                    ResolveAssertion(id, asr);
                    break;
                default:
                    Reject(id, "NotAllowedError", "Unexpected credential type.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Reject(id, "NotAllowedError", ex.Message);
        }
    }

    [Export("authorizationController:didCompleteWithError:")]
    public void DidComplete(ASAuthorizationController controller, NSError error)
    {
        // WebAuthn maps every failure (including the user dismissing the sheet,
        // ASAuthorizationError.Canceled) to NotAllowedError, which the web app treats as "cancelled".
        Reject(_pendingId, "NotAllowedError", error.LocalizedDescription ?? "Passkey operation failed.");
    }

    private void ResolveRegistration(string? id, ASAuthorizationPlatformPublicKeyCredentialRegistration reg)
    {
        var result = new
        {
            id = B64Url.Encode(reg.CredentialId.ToArray()),
            rawId = B64Url.Encode(reg.CredentialId.ToArray()),
            authenticatorAttachment = "platform",
            response = new
            {
                clientDataJSON = B64Url.Encode(reg.RawClientDataJson.ToArray()),
                attestationObject = B64Url.Encode(reg.RawAttestationObject.ToArray()),
                transports = new[] { "internal", "hybrid" },
            },
            clientExtensionResults = new { },
        };
        Resolve(id, JsonSerializer.Serialize(result));
    }

    private void ResolveAssertion(string? id, ASAuthorizationPlatformPublicKeyCredentialAssertion asr)
    {
        var result = new
        {
            id = B64Url.Encode(asr.CredentialId.ToArray()),
            rawId = B64Url.Encode(asr.CredentialId.ToArray()),
            authenticatorAttachment = "platform",
            response = new
            {
                clientDataJSON = B64Url.Encode(asr.RawClientDataJson.ToArray()),
                authenticatorData = B64Url.Encode(asr.RawAuthenticatorData.ToArray()),
                signature = B64Url.Encode(asr.Signature.ToArray()),
                userHandle = asr.UserId is { } uid ? B64Url.Encode(uid.ToArray()) : null,
            },
            clientExtensionResults = new { },
        };
        Resolve(id, JsonSerializer.Serialize(result));
    }

    // ---- presentation anchor -----------------------------------------------------------------

    [Export("presentationAnchorForAuthorizationController:")]
    public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
    {
        if (_web.TryGetTarget(out var w) && w.Window is { } win) return win;
        return UIApplication.SharedApplication.Windows.FirstOrDefault(x => x.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.First();
    }

    // ---- JS callback plumbing ----------------------------------------------------------------

    private void Resolve(string? id, string resultJson)
    {
        if (string.IsNullOrEmpty(id)) return;
        Eval($"window.__lamaPasskeyResolve({Js(id)}, {Js(resultJson)});");
    }

    private void Reject(string? id, string name, string message)
    {
        if (string.IsNullOrEmpty(id)) return;
        Eval($"window.__lamaPasskeyReject({Js(id)}, {Js(name)}, {Js(message)});");
    }

    private void Eval(string js)
    {
        if (!_web.TryGetTarget(out var w)) return;
        w.InvokeOnMainThread(() => w.EvaluateJavaScript(js, null));
    }

    // JSON-encode a string into a safe JS string literal (handles quotes/newlines/unicode).
    private static string Js(string value) => JsonSerializer.Serialize(value);

    private static bool TryGetUserVerification(JsonElement pk, out ASAuthorizationPublicKeyCredentialUserVerificationPreference uv)
    {
        uv = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Preferred;
        string? v = null;
        if (pk.TryGetProperty("userVerification", out var direct) && direct.ValueKind == JsonValueKind.String)
            v = direct.GetString();
        else if (pk.TryGetProperty("authenticatorSelection", out var sel) && sel.ValueKind == JsonValueKind.Object
                 && sel.TryGetProperty("userVerification", out var nested) && nested.ValueKind == JsonValueKind.String)
            v = nested.GetString();

        if (v is null) return false;
        uv = v switch
        {
            "required" => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required,
            "discouraged" => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Discouraged,
            _ => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Preferred,
        };
        return true;
    }
}

/// <summary>base64url (no padding) — the encoding WebAuthn uses on the wire.</summary>
internal static class B64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
