# Passkeys in the mobile app (WebView)

The tenant web app's passkey (WebAuthn) feature works in real browsers but not in the embedded
WebView, because a WebView is **not** a browser and does not get WebAuthn for free.

- **Android** — System WebView hides `window.PublicKeyCredential` unless the host app opts in. The web
  app's feature-detection then returns false and the passkey buttons stay hidden.
- **iOS** — WKWebView exposes `window.PublicKeyCredential` (so the button shows) but refuses to run the
  ceremony for embedded content: `navigator.credentials.get/create` reject with
  *"not allowed by the user agent or the platform in the current context"* (`NotAllowedError`).

The backend uses a **per-host Relying Party ID** for the web (`Fido2Factory` derives `rp.id` from the
request `Origin`). For the **mobile app** it now uses a **single fixed RP ID** instead (issued for
requests carrying `X-Client-Type: mobile`), decoupled from the tenant host — this is what lets passkeys
work on every tenant, **including custom domains**, from one shipped build. See "Fixed mobile RP ID".

---

## What the app now does

| Platform | App-side change | Status |
|---|---|---|
| Android | `MauiProgram.ConfigureWebView` enables `WebSettingsCompat.setWebAuthenticationSupport(FOR_APP)` via `Xamarin.AndroidX.WebKit`, which un-hides `window.PublicKeyCredential`. | Code complete, builds. |
| iOS | Native bridge: a document-start JS shim (`PasskeyBridge.ShimJs`) overrides `navigator.credentials.create/get` and routes them to `IosPasskeyBridge`, which runs `ASAuthorizationController` (Face ID / Touch ID + iCloud Keychain) using the per-request `rp.id`. | Code complete; **must be built on macOS** (iOS only builds there) and needs the items below. |

No web/frontend changes are required — the capability checks light up once the platform APIs work.

---

## Fixed mobile RP ID (why custom domains work)

iOS Associated Domains are baked into the signed app binary, so a per-tenant RP ID would need a new
build for every tenant / custom domain. Instead the backend issues mobile passkeys under **one fixed
RP ID**, decoupled from the tenant host. In both platform paths the RP ID is validated against an
*app↔domain* association (iOS entitlement / Android asset links) — **not** the WebView's current
origin — so a fixed RP ID works on every tenant, including custom domains.

**Backend** (`Fido2Factory` + `PasskeyMobileSettings`, configured under `Identity:Passkeys:Mobile`):

```jsonc
"Identity": {
  "Passkeys": {
    "Mobile": {
      "RpId": "lamaerp.com",
      "Origins": [
        "https://lamaerp.com",                                  // iOS native passkeys
        "android:apk-key-hash:<base64url-sha256-of-signing-cert>" // Android WebView (FOR_APP)
      ]
    }
  }
}
```

- Empty `RpId` ⇒ mobile falls back to the per-host behaviour (feature off). Web is never affected.
- `Origins` is the allow-list of `clientDataJSON.origin` values the authenticators report. iOS native
  reports `https://{RpId}`; Android `FOR_APP` reports `android:apk-key-hash:{…}` (add one per signing
  cert — Play App Signing key, debug/upload key). To compute the Android value: base64url-encode the
  raw SHA-256 **bytes** of the signing cert (not the colon-hex string).
- Mobile passkeys live under this RP ID and are therefore **separate** from browser-created per-host
  passkeys — users enrol once on mobile. Credentials are still stored/validated per-tenant DB.

**App:** the bridge passes the backend's `rp.id` straight through (no code change). The entitlement and
the `.well-known` files only need the **one** fixed host (`lamaerp.com`), not every tenant domain.

---

## Required infra (NOT in this repo — backend / Apple portal)

With the fixed mobile RP ID, the association files only need to live on the **one** RP host
(`https://lamaerp.com`), not on every tenant domain.

### 1. Android — Digital Asset Links (`assetlinks.json`)
Serve at `https://lamaerp.com/.well-known/assetlinks.json` (the fixed RP host):

```json
[{
  "relation": ["delegate_permission/common.get_login_creds"],
  "target": {
    "namespace": "android_app",
    "package_name": "com.lamaerp.app",
    "sha256_cert_fingerprints": ["<APP SIGNING CERT SHA-256>"]
  }
}]
```

- `sha256_cert_fingerprints` must list **every** signing key the installed APK may use: the Google Play
  **App Signing** key (Play Console → Test and release → App integrity) and any upload/debug key used
  for sideloaded test builds. You can list multiple.
- The Android `FOR_APP` WebAuthn call is attributed to the app and validated against this file on the
  RP host — independent of which tenant (subdomain or custom domain) the WebView is showing. So this
  one file covers all tenants.

### 2. iOS — Apple App Site Association (`apple-app-site-association`)
Serve at `https://lamaerp.com/.well-known/apple-app-site-association`
(content-type `application/json`, **no** `.json` extension, no redirects):

```json
{
  "webcredentials": { "apps": ["<TEAM_ID>.com.lamaerp.app"] }
}
```

Replace `<TEAM_ID>` with your Apple Developer **Team ID** (Membership page). This is the one value still
needed to finish the iOS side.

### 3. iOS — entitlement + capability
- [Platforms/iOS/Entitlements.plist](Platforms/iOS/Entitlements.plist) declares
  `webcredentials:lamaerp.com` (the fixed RP host) and is wired via `CodesignEntitlements` in the csproj.
- Enable the **Associated Domains** capability on the `com.lamaerp.app` App ID in the Apple Developer
  portal, and regenerate the provisioning profile.

> Because the mobile RP ID is fixed (not per-tenant), this single static entry covers **all** tenants
> including custom domains — no per-domain rebuild. If you ever change `PasskeyMobileSettings.RpId`,
> update this entry and the `.well-known` host to match and ship a new build.

---

## Building / testing

- **Android:** `dotnet build -f net10.0-android -c Release`. Install, sign in on a `*.lamaerp.com`
  tenant whose host serves `assetlinks.json`, then Profile → Passkeys → Add, and passkey login.
  Requires a reasonably current Android System WebView.
- **iOS (macOS only):** `dotnet build -f net10.0-ios -c Release`. The `Platforms/iOS/*` sources and the
  entitlement only compile/sign there. Verify the AuthenticationServices binding member names if the
  compiler flags them (Apple property casing vs the .NET binding) — they're the most likely fixups.

---

## Key files (this repo)

| Concern | File |
|---|---|
| Android WebAuthn enablement + iOS shim/handler wiring | [MauiProgram.cs](MauiProgram.cs) |
| iOS WebAuthn → native JS shim | [Services/PasskeyBridge.cs](Services/PasskeyBridge.cs) |
| iOS native ASAuthorization handler | [Platforms/iOS/IosPasskeyBridge.cs](Platforms/iOS/IosPasskeyBridge.cs) |
| iOS Associated Domains entitlement | [Platforms/iOS/Entitlements.plist](Platforms/iOS/Entitlements.plist) |
