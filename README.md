# PhoneDrop

**English** · [Čeština](README.cs.md) · **Website: [phonedrop.apexpiksel.cz](https://phonedrop.apexpiksel.cz)**

Drag a file into a small window on Windows and it lands on your Android phone. APKs open the system
installer right away: one tap on **Install** and you're done. No cable, no `adb`, no wireless debugging.

- **APK** → the phone opens the Android installer (a single tap to confirm; silent installs aren't possible without root or ADB).
- **Split APKs** (`.apks` from SAI or bundletool, `.xapk`) → all the APKs inside go into one install.
- **Anything else** → saved to `Downloads/Drop`, with a notification that opens it. Drop several files at once or
  a whole folder: it lands as `Downloads/Drop/<folder>/…` with its subfolders.
- **Phone → PC:** *Share › PhoneDrop* in any app (or *Send to PC* in PhoneDrop) and the file lands in
  `Downloads` on the PC. The PC picks it up by itself, so nothing has to be opened in the Windows firewall;
  if the PC is off, the file waits on the phone (up to 3 days).
- Works on your local network. The PC finds the phone by itself (mDNS), even when its IP address changes.
- Light and dark theme, English and Czech, on both sides.
- Both apps update themselves from GitHub Releases (the download is checked against `SHA256SUMS.txt`;
  Android additionally only accepts an APK signed with the same key).
- Send feedback straight from either app (⋯ → *Send feedback* on Windows, *Feedback* on the phone), with up to
  three screenshots: *Window snapshot*, *Add image…* or Ctrl+V on Windows, the photo picker on the phone.
  Images are shrunk and stripped of metadata (EXIF location etc.) before they leave the device.
- Every release is built from this repository by GitHub Actions, not on someone's PC.

```
android/     receiver for Android (Java, no libraries, Android 10+)
windows/     the drop window for Windows (.NET 10, WinForms) + a command line
website/     the project page (static HTML, no build step)
PROTOCOL.md  the HTTP protocol between the two (in Czech)
```

## Getting started

1. **Phone:** install `PhoneDrop.apk`, open it and allow notifications. It shows its address and a **PIN**.
   Tap *Allow* next to *Install apps* and *Run in background* (otherwise the phone stops receiving when idle).
   (The PIN is only a fallback: pairing normally takes a QR code, see step 2.)
2. **Windows:** run `PhoneDrop-…-win-x64.exe` – .NET is built in, nothing to install. (If you already have the
   [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), the `…-small.exe` does the same in 0.4 MB.)
   The drop box shows a **QR code**: on the phone tap *Scan QR code* (or point the camera at it) and confirm. Done.
   Clicking the QR code lets you pair with the PIN instead (only while the phone's PhoneDrop screen is open; after five
   wrong PINs the phone picks a new one and waits a minute).
3. Drop files into the window. Closing it keeps PhoneDrop in the notification area; quit from the ⋯ menu.
4. Optional: ⋯ → *Explorer: Send to › Phone* adds **Send to › Phone (PhoneDrop)** to the right-click menu.
5. The other way: on the phone, *Share › PhoneDrop to PC*. Files go to `Downloads` (⋯ → *Folder for files from the
   phone…* to change it, ⋯ → *Open files from the phone* to see them).

### How APK installs work

- If PhoneDrop is open on the phone, the *Install / Update* dialog appears immediately.
- If it's in the background, Android doesn't let apps open windows on their own, so a notification
  **Install … – tap to confirm** appears instead; tapping it opens the dialog.
- The very first time, Android asks you to allow "Install unknown apps" for PhoneDrop.
- Google Play Protect may offer to scan an unfamiliar APK: *More details → Install without scanning*.
- The Windows window waits for the result and shows *Installed ✓ / cancelled / failed (reason)*.

## Building

**Android** (Gradle 9.1, AGP 9.0.1, JDK 21+ – the one bundled with Android Studio works):

```bash
cd android
./gradlew assembleRelease
```

`android/local.properties` (not in git) must point at the Android SDK with forward slashes, e.g. `sdk.dir=C:/Android/sdk`.

Release builds are signed with the key described in `android/keystore.properties` (not in git):

```
storeFile=C:/path/to/phonedrop-release.jks
storePassword=…
keyAlias=phonedrop
keyPassword=…
```

Without it the release falls back to the debug key, which is fine for testing but must not be published:
an APK signed with a different key can't update an installed one.

**Windows** (.NET 10 SDK):

```bash
cd windows/PhoneDrop
# self-contained (~25 MB, .NET inside, trimmed)
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o ../publish
# small (~0.4 MB, needs the .NET 10 Desktop Runtime)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../publish-small
```

`PhoneDrop.exe --ui-selftest <folder>` renders every window into PNGs (used by CI to catch trimming problems).

### Releases

Pushing a tag `vX.Y.Z` runs [`.github/workflows/release.yml`](.github/workflows/release.yml): it builds both exes
and the APK, signs the APK with the key from the repository secrets (`ANDROID_KEYSTORE_BASE64`,
`ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`), checks the certificate fingerprint,
writes `SHA256SUMS.txt` and publishes the GitHub Release. Release notes come from
`.github/release-notes/vX.Y.Z.md` when it exists.

## Command line

```
PhoneDrop.exe --discover                 phones on the network (mDNS)
PhoneDrop.exe --pair 192.168.1.20:8765 123456
PhoneDrop.exe --send app.apk photo.jpg   send files (waits for APK installs)
PhoneDrop.exe --status                   check the connection
PhoneDrop.exe --sendto on|off            the Explorer "Send to" entry
PhoneDrop.exe --lang auto|en|cs          language
PhoneDrop.exe --theme auto|light|dark    appearance
PhoneDrop.exe --update                   check for a new version and install it
```

Testing with the Android emulator: it sits behind NAT, so forward the port and pair with `127.0.0.1`:
`adb forward tcp:8765 tcp:8765`.

## Limitations

- Local network only. Traffic is plain HTTP with a pairing token, fine for a home Wi-Fi, not for public networks.
  A relay for use outside the home network is planned (see PROTOCOL.md).
- OBB data inside `.xapk` files is skipped (Android 11+ doesn't let apps write into another app's `obb` folder).
  `.apkm` (encrypted APKMirror bundles) isn't supported.

## License

[MIT](LICENSE)
