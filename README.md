# PhoneDrop

**English** · [Čeština](README.cs.md)

Drag a file into a small window on Windows and it lands on your Android phone. APKs open the system
installer right away: one tap on **Install** and you're done. No cable, no `adb`, no wireless debugging.

- **APK** → the phone opens the Android installer (a single tap to confirm; silent installs aren't possible without root or ADB).
- **Anything else** → saved to `Downloads/Drop`, with a notification that opens it.
- Works on your local network. The PC finds the phone by itself (mDNS), even when its IP address changes.
- Light and dark theme, English and Czech, on both sides.

```
android/     receiver for Android (Java, no libraries, Android 10+)
windows/     the drop window for Windows (.NET 10, WinForms) + a command line
website/     the project page (static HTML, no build step)
PROTOCOL.md  the HTTP protocol between the two (in Czech)
```

## Getting started

1. **Phone:** install `PhoneDrop.apk`, open it and allow notifications. It shows its address and a **PIN**.
   Tap *Allow* next to *Install apps* and *Run in background* (otherwise the phone stops receiving when idle).
2. **Windows:** run `PhoneDrop.exe` (needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)).
   Pick the phone from the list (or type its address) and enter the PIN.
3. Drop files into the window. Closing it keeps PhoneDrop in the notification area; quit from the ⋯ menu.
4. Optional: ⋯ → *Explorer: Send to › Phone* adds **Send to › Phone (PhoneDrop)** to the right-click menu.

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

**Windows** (.NET 10 SDK):

```bash
cd windows/PhoneDrop
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../publish
```

## Command line

```
PhoneDrop.exe --discover                 phones on the network (mDNS)
PhoneDrop.exe --pair 192.168.1.20:8765 123456
PhoneDrop.exe --send app.apk photo.jpg   send files (waits for APK installs)
PhoneDrop.exe --status                   check the connection
PhoneDrop.exe --sendto on|off            the Explorer "Send to" entry
PhoneDrop.exe --lang auto|en|cs          language
PhoneDrop.exe --theme auto|light|dark    appearance
```

Testing with the Android emulator: it sits behind NAT, so forward the port and pair with `127.0.0.1`:
`adb forward tcp:8765 tcp:8765`.

## Limitations

- Local network only. Traffic is plain HTTP with a pairing token, fine for a home Wi-Fi, not for public networks.
  A relay for use outside the home network is planned (see PROTOCOL.md).
- Folders and split APKs (`.apks`, `.xapk`) aren't supported yet.

## License

[MIT](LICENSE)
