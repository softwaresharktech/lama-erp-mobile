# Deployment — TestFlight (iOS)

How to build and publish the **LamaERP Mobile** (.NET MAUI) app to TestFlight.

> **iOS builds require a Mac.** They cannot be produced on Windows — the `csproj` only
> adds the `net10.0-ios` target framework when building on macOS. Do all of the steps
> below on a Mac.

---

## App identifiers (reference)

| Thing | Value |
|---|---|
| Bundle ID | `com.lamaerp.app` |
| App Store Connect App ID | `6778825050` |
| Apple Team ID | `4LGLWD23W8` |
| Team name | SOFTWARE SHARK TECH PVT LTD |
| Distribution certificate | `Apple Distribution: SOFTWARE SHARK TECH PVT LTD (4LGLWD23W8)` |
| App Store provisioning profile | `LamaERP App Store` |

---

## One-time setup

### 1. Signing certificate (already created)

A distribution certificate must exist in the Mac keychain. Verify with:

```bash
security find-identity -v -p codesigning
```

You should see a line containing **`Apple Distribution: SOFTWARE SHARK TECH PVT LTD (4LGLWD23W8)`**.
If missing, create one via Xcode → Settings → Accounts → Manage Certificates → `+` → Apple Distribution,
or via the Developer portal (Certificates → `+` → Apple Distribution) using a CSR.

> ⚠️ **Back up the private key.** In Keychain Access, right-click the cert → Export → `.p12`
> (with a password). Without it you cannot sign updates with the same certificate.

### 2. App Store provisioning profile

The profile **must** be an **App Store** distribution profile bound to the **explicit** App ID
`com.lamaerp.app` (not a wildcard, not a different bundle ID).

Create it at https://developer.apple.com/account/resources/profiles/list:

1. `+` → Distribution → **App Store Connect** → Continue
2. App ID: select the one whose bundle ID is **`com.lamaerp.app`** → Continue
3. Certificate: **Apple Distribution: SOFTWARE SHARK TECH PVT LTD** → Continue
4. Name it **`LamaERP App Store`** → Generate → Download

Install it (double-clicking does nothing on recent macOS — copy it manually):

```bash
mkdir -p ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/
mkdir -p ~/Library/MobileDevice/Provisioning\ Profiles/
cp ~/Downloads/*.mobileprovision ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/
cp ~/Downloads/*.mobileprovision ~/Library/MobileDevice/Provisioning\ Profiles/
```

Verify it has the correct App ID (`4LGLWD23W8.com.lamaerp.app`) and is App Store type
(`get-task-allow => false`, no `ProvisionedDevices`):

```bash
for f in ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/*.mobileprovision; do
  echo "=== $f ==="
  security cms -D -i "$f" | plutil -p - | grep -E '"Name"|application-identifier|get-task-allow|ProvisionedDevices'
done
```

### 3. App Store Connect API key (for uploading)

At https://appstoreconnect.apple.com/access/integrations/api (Team Keys tab):

1. `+` → name it (e.g. "Upload Key") → role **App Manager** → Generate
2. Copy the **Issuer ID** (shown above the table — shared by all keys)
3. Copy the **Key ID** (the row)
4. **Download the `.p8`** (one-time only) and place it where `altool` looks:

```bash
mkdir -p ~/.appstoreconnect/private_keys
mv ~/Downloads/AuthKey_*.p8 ~/.appstoreconnect/private_keys/
```

Keep these values out of version control. Store them locally (e.g. a `.env` or password
manager), and substitute them into the upload command below:
- **Key ID:** `<YOUR_KEY_ID>`
- **Issuer ID:** `<YOUR_ISSUER_ID>`
- **`.p8` file:** `~/.appstoreconnect/private_keys/AuthKey_<YOUR_KEY_ID>.p8` (never commit this)

---

## ⚠️ Critical: do NOT keep the project in an iCloud-synced folder

iCloud Drive's "Desktop & Documents" sync continuously stamps build output with extended
attributes (`com.apple.provenance`, `com.apple.FinderInfo`, `com.apple.fileprovider.fpfs#P`).
`codesign` rejects these with:

```
resource fork, Finder information, or similar detritus not allowed
```

Stripping the attributes does not help because iCloud re-adds them mid-build. **Keep the repo
in a plain local folder** (e.g. `~/dev/lama-erp-mobile`), NOT under `~/Desktop`, `~/Documents`,
or `~/Library/Mobile Documents`.

```bash
# one-time move out of the synced location
mkdir -p ~/dev
mv ~/Desktop/.../lama-erp-mobile ~/dev/lama-erp-mobile
```

---

## Release build steps (each release)

```bash
cd ~/dev/lama-erp-mobile

# 1. Bump the build number (App Store Connect rejects duplicates).
#    Edit LamaERP.Mobile.WebApp.csproj -> <ApplicationVersion> (increment by 1).

# 2. Clean previous output (optional but recommended)
rm -rf bin/Release/net10.0-ios obj/Release/net10.0-ios

# 3. Build & sign a release .ipa
dotnet publish -f net10.0-ios -c Release \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:ArchiveOnBuild=true \
  -p:CodesignKey="Apple Distribution: SOFTWARE SHARK TECH PVT LTD (4LGLWD23W8)" \
  -p:CodesignProvision="LamaERP App Store"
```

Output `.ipa`:

```
bin/Release/net10.0-ios/ios-arm64/publish/LamaERP.Mobile.WebApp.ipa
```

---

## Upload to TestFlight

```bash
xcrun altool --upload-app -t ios \
  -f ~/dev/lama-erp-mobile/bin/Release/net10.0-ios/ios-arm64/publish/*.ipa \
  --apiKey <YOUR_KEY_ID> \
  --apiIssuer <YOUR_ISSUER_ID>
```

A successful run ends with `UPLOAD SUCCEEDED with no errors`.

> Alternative GUI: the **Transporter** app (Mac App Store) — sign in with the Apple ID and
> drag the `.ipa` in. No API key needed.

---

## Finish in App Store Connect

1. https://appstoreconnect.apple.com → My Apps → **LamaERP** → **TestFlight** tab.
2. Build shows **"Processing"** for ~5–15 min (longer for the first build). Apple emails when ready.
3. Click the build → answer **Export Compliance**: app uses HTTPS only →
   **Yes** (uses encryption) → **Yes** (qualifies for exemption, standard encryption).
4. **Internal testing** (instant, no review): add testers (team members, up to 100).
5. **External testing** (one-time Beta App Review, ~1 day): create a group, add tester emails
   or enable a public link, submit for review.

Testers install the **TestFlight** app from the App Store, accept the invite, and tap Install.

---

## Troubleshooting (issues we actually hit)

| Error | Cause | Fix |
|---|---|---|
| `bundle identifier 'com.lamaerp.app' does not match specified provisioning profile` | Profile was bound to the wrong App ID (`4LGLWD23W8.lamaerp`) | Regenerate the profile against the **explicit** `com.lamaerp.app` App ID |
| `resource fork, Finder information, or similar detritus not allowed` | iCloud sync (project on Desktop) re-stamping extended attributes | Move the project to a non-synced folder (`~/dev/...`) |
| Double-clicking `.mobileprovision` does nothing | Removed in recent Xcode | Copy the file into the Provisioning Profiles folders manually (see setup) |
| `XC0618 UseSafeArea deprecated` warning | Deprecated MAUI API in `Pages/WebAppPage.xaml` | Harmless warning; clean up later |

---

## Quick checklist for the next release

- [ ] On a Mac, repo in a non-iCloud folder (`~/dev/lama-erp-mobile`)
- [ ] Bump `<ApplicationVersion>` in `LamaERP.Mobile.WebApp.csproj`
- [ ] `dotnet publish` (command above)
- [ ] `xcrun altool --upload-app` (command above)
- [ ] App Store Connect → TestFlight → set export compliance → assign testers
