# LamaERP Mobile (WebView shell)

A .NET MAUI **WebView** app for LamaERP — an Odoo-style thin native shell. The user enters their
organization address, the app resolves the tenant against the backend, then loads that tenant's
own web app (login + everything after) inside a WebView. The native layer only handles the
identifier step, multi-account switching, and securely persisting the session.

Accounts are **per user, not per tenant** — several users of the *same* tenant can be signed in at
once and switched between, each keeping its own saved session.

- **Project:** `LamaERP.Mobile.WebApp` · **App ID:** `com.lamaerp.mobile`
- **Targets:** `net10.0-android`, `net10.0-windows10.0.19041.0` (`net10.0-ios` on macOS)
- **Production host:** each org is served at `https://{domain}` (e.g. `myorg.lamaerp.com`),
  which also routes `/api/*` and serves the Vue tenant SPA.

---

## Flow

1. **IdentifierPage** — brand logo + organization address (`myorg.lamaerp.com` or bare `myorg`).
   On Continue it calls `GET /api/auth/tenant/resolve` with `X-Tenant-Id` to validate/connect the
   tenant, then saves it as an account.
2. **WebAppPage** — full-screen WebView loading `https://{domain}/`. The tenant SPA renders its own
   login page and the entire app flow runs as web. There is **no native top bar**; switching
   organizations happens from the web portal's user menu (**Switch account**).
3. **AccountsPage** — slides up from the bottom (logo + "Choose an account" + saved accounts;
   Cancel left / **+** right). **+** reopens the identifier screen to add another org.

### How auth / session works
- The tenant SPA keeps the JWTs in **httpOnly cookies** (`access_token`, `refresh_token`) — not in
  localStorage. The WebView cookie jar persists them across launches.
- Injected JS (`WebBridge`) tags every API call with **`X-Client-Type: mobile`** so the backend
  grants the longer mobile refresh-token lifetime on login/refresh, and exposes `window.LamaMobile`
  (Switch account hook + `lamaerpapp://` command scheme) and a touch **pull-to-refresh**.
- All accounts of one tenant share an origin — and therefore **one cookie jar** — so only one session
  can be live in the jar at a time. `IWebSession` keeps a per-account encrypted copy in SecureStorage
  (keyed by `Account.Id`) and **swaps** the active account's cookies in/out of the jar on every load:
  - `SwitchToAsync(accountId, url)` — clears the host's cookies, then loads that account's saved set.
  - `CaptureAsync(accountId, url)` — saves the jar's current cookies (incl. freshly-issued tokens).
    It **ignores an auth-less jar** (e.g. the login page) so visiting login never wipes a saved session.
  - `ClearAsync(accountId, url)` — empties the jar + forgets the saved session (fresh login / logout).
  - Platform impls: Android `CookieManager`, iOS `WKHTTPCookieStore`, Windows WebView2
    `CoreWebView2.CookieManager` (the live control is handed to the session by the page).

### Multiple users of the same tenant
- Each sign-in is a distinct **`Account`** with a stable GUID `Id` (not the tenant), so two users of
  one tenant coexist. `SessionStore` and the avatar cache are keyed by that `Id`.
- **Adding** a user (identifier **+**) mints a new account and force-clears the jar, so the tenant's
  **login page** shows instead of resuming the existing user.
- On reaching `/portal`, the shell asks the backend who actually signed in — **`GET /api/auth/me`**
  (id, name, email) and **`GET /api/users/me`** (photo), sent with the captured cookies
  (`UserProfileService`). The user **id** de-dupes accounts: signing in again as a user that's already
  saved shows an **"Already added"** notice instead of creating a duplicate.
- The account picker shows the **user's name**, an **email · tenant** second line, and their **photo**
  (initial-letter fallback) so two users of one tenant are distinguishable.

### Key files
| Area | File |
|------|------|
| Origins / prod vs local | `Services/AppConfig.cs` |
| Multi-account store (keyed by `Account.Id`) | `Services/AccountStore.cs`, `Services/Account.cs` |
| Per-account session swap + SecureStorage backup | `Services/SessionStore.cs`, `Services/IWebSession.cs`, `Services/AuthCookies.cs`, `Platforms/*/...WebSession.cs` |
| Signed-in user identity + cached avatar (`/api/auth/me`, `/api/users/me`) | `Services/UserProfileService.cs` |
| Injected JS (mobile header, switch hook, pull-to-refresh) | `Services/WebBridge.cs` |
| Pages | `Pages/IdentifierPage.*`, `Pages/WebAppPage.*`, `Pages/AccountsPage.*` |
| Web portal hooks (separate repo) | `frontend/tenant/src/components/PortalTopBar.vue`, `frontend/tenant/src/pages/apps/AppLauncher.vue` |

> The web-side changes (Switch account item, mobile menu list + search) live in the **tenant
> frontend** repo and only appear once that frontend is rebuilt and deployed to `lamaerp.com`.
> The native shell works against the live site immediately.

---

## Prerequisites

```pwsh
dotnet --version            # 10.x
dotnet workload list        # needs: maui-android, maui-windows
# install if missing:
dotnet workload install maui-android maui-windows
```

`adb` (Android Platform Tools) lives at:
`C:\Users\Bipin\AppData\Local\Android\Sdk\platform-tools\adb.exe`
Add it to PATH, or set once per shell:

```pwsh
$adb = "C:\Users\Bipin\AppData\Local\Android\Sdk\platform-tools\adb.exe"
```

---

## Build & run — Windows

```pwsh
# Build (Release = talks to production https://{domain}; Debug = AppConfig.LocalOrigin)
dotnet build .\LamaERP.Mobile.WebApp.csproj -f net10.0-windows10.0.19041.0 -c Release

# Run the built exe
$exe = ".\bin\Release\net10.0-windows10.0.19041.0\win-x64\LamaERP.Mobile.WebApp.exe"
Start-Process $exe

# Kill a running instance (do this before rebuilding — the exe locks otherwise)
Get-Process -Name "LamaERP.Mobile.WebApp" -ErrorAction SilentlyContinue | Stop-Process -Force

# Kill + rebuild + relaunch (one-liner)
Get-Process -Name "LamaERP.Mobile.WebApp" -EA SilentlyContinue | Stop-Process -Force
dotnet build .\LamaERP.Mobile.WebApp.csproj -f net10.0-windows10.0.19041.0 -c Release
Start-Process ".\bin\Release\net10.0-windows10.0.19041.0\win-x64\LamaERP.Mobile.WebApp.exe"
```

---

## Build & run — Android (USB device)

### 1. See connected devices
```pwsh
& $adb devices -l                 # should list your device as "device" (not "unauthorized")
& $adb kill-server; & $adb start-server   # if the list is empty, restart the daemon and replug
```
If a device shows `unauthorized`, tap **Allow USB debugging** on the phone.

### 2. Build the APK
```pwsh
dotnet build .\LamaERP.Mobile.WebApp.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
# output: .\bin\Release\net10.0-android\com.lamaerp.mobile-Signed.apk
```

### 3. Install onto the phone
```pwsh
$apk = ".\bin\Release\net10.0-android\com.lamaerp.mobile-Signed.apk"
& $adb -s 809edc7f install -r $apk          # -r = reinstall/keep data; -s picks the device by serial
```

### Build + install + launch in one step
```pwsh
dotnet build .\LamaERP.Mobile.WebApp.csproj -f net10.0-android -c Release -t:Run `
  -p:AndroidPackageFormat=apk -p:AdbTarget="-s 809edc7f"
```

### Useful adb commands
```pwsh
& $adb devices -l                                   # list devices + model
& $adb -s 809edc7f shell getprop ro.product.model   # device model
& $adb uninstall com.lamaerp.mobile                 # remove the app
& $adb -s 809edc7f logcat -s DOTNET MonoDroid Web Console   # app + WebView logs
& $adb -s 809edc7f shell am start -n com.lamaerp.mobile/crc*.MainActivity  # launch
```

### MIUI / HyperOS (Xiaomi / Redmi / POCO) install gotcha
`INSTALL_FAILED_USER_RESTRICTED: Install canceled by user` means MIUI blocked the USB install.
On the phone → **Settings → Additional settings → Developer options**:
- Enable **Install via USB**
- Enable **USB debugging (Security settings)** *(needs a SIM + Mi-account sign-in)*
- Set USB mode to **File transfer (MTP)**
Then re-run the install. Approve any on-device popup.

---

## Configuration

| What | Where |
|------|-------|
| Production vs local origin | `Services/AppConfig.cs` → `LocalOrigin` (Debug) / `https://{domain}` (Release) |
| Mobile client header | `AppConfig.ClientTypeHeader` / `ClientTypeValue` (`X-Client-Type: mobile`) |
| Android cleartext for local dev | `Platforms/Android/Resources/xml/network_security_config.xml` (keep in sync with `LocalOrigin`) |

- **Release** builds target production `https://{domain}` — enter a real org subdomain
  (bare `lamaerp.com` is the platform host, not a tenant, so it returns 404 on resolve).
- **Debug** builds target `AppConfig.LocalOrigin`. The entered domain is still sent as
  `X-Tenant-Id`. Auth cookies are `Secure`, so they are only stored over **HTTPS** — point the dev
  origin at an https endpoint for a faithful login/persistence test.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `adb devices` empty | Replug USB, set mode to File transfer, `adb kill-server; adb start-server`, accept the on-phone prompt |
| `INSTALL_FAILED_USER_RESTRICTED` | MIUI — enable *Install via USB* + *USB debugging (Security settings)* (see above) |
| Windows build error: file in use / locked exe | Kill the running app first (`Stop-Process` above) |
| Resolve returns 404 | You entered the platform host; use an actual org subdomain (`myorg.lamaerp.com`) |
| Logged out after relaunch | Cookies are `Secure`; only persist over HTTPS. Use a real https origin |
| Adding a 2nd user of a tenant lands on the 1st user's portal | The add path force-clears the jar to show login — if it doesn't, a stale build is running; rebuild |
| Account shows the tenant name, no email/photo | `GET /api/auth/me` / `/api/users/me` failed for that session — name/photo backfill on next portal load |
| Switch account / mobile list missing in web | Rebuild & redeploy the tenant frontend to `lamaerp.com` |
