# LamaERP Mobile (WebView shell)

A .NET MAUI **WebView** app for LamaERP — an Odoo-style thin native shell. The user enters their
organization address, the app resolves the tenant against the backend, then loads that tenant's
own web app (login + everything after) inside a WebView. The native layer only handles the
identifier step, multi-account switching, and securely persisting the session.

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
- The httpOnly cookies are mirrored natively into **SecureStorage** after each navigation
  (`IWebSession.CaptureAsync`) and restored before load (`RestoreAsync`); switching restores the
  selected account's session. Platform impls: Android `CookieManager`, iOS `WKHTTPCookieStore`,
  Windows (WebView2 persists its own jar).

### Key files
| Area | File |
|------|------|
| Origins / prod vs local | `Services/AppConfig.cs` |
| Multi-account store | `Services/AccountStore.cs`, `Services/Account.cs` |
| SecureStorage cookie backup | `Services/SessionStore.cs`, `Services/IWebSession.cs`, `Platforms/*/...WebSession.cs` |
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
| Switch account / mobile list missing in web | Rebuild & redeploy the tenant frontend to `lamaerp.com` |
