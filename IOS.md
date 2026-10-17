# iOS

The iOS head (`src/Reyfen.Timbratune.iOS`) builds the same app for iPhone and iPad. Like the
Android head it's not in `Timbratune.slnx`. iOS apps can only be built and signed on macOS with
Xcode, so the build runs on GitHub Actions (`.github/workflows/ios.yml`) and goes to TestFlight.

| | |
|---|---|
| Bundle id | `pl.eleanorakagner.timbratune` (App ID "Voice Trainer") |
| Team | `HJJG5KZRGJ` |
| Profile | `Timbratune AppStore` (App Store distribution) |
| Home-screen name | Timbratune |
| Minimum iOS | 15.0, iPhone and iPad, devices only (arm64) |
| Version | `<Version>` in `Directory.Build.props`; build number = the workflow's run number |

## Building

On every push to `feature/ios` and every `v*` tag, the workflow builds the signed `.ipa`, uploads
it to App Store Connect, and keeps it as a run artifact. After Apple processes it (5–30 min), it
shows in TestFlight. Once the workflow is on `main`, it can also be started by hand (Actions › iOS ›
Run workflow), with or without the upload.

On a Mac: install Xcode 26, the .NET 10 SDK and `dotnet workload install ios`, put the certificate
in the keychain and the profile in Xcode, then run `scripts/build-ios.sh [--build-number N] [--upload]`.

## Secrets (one-time setup)

Repository › Settings › Secrets and variables › Actions › New repository secret:

| Secret | What |
|---|---|
| `IOS_P12_BASE64` | The Apple Distribution certificate (`.p12`), base64 |
| `IOS_P12_PASSWORD` | Its password |
| `IOS_PROFILE_BASE64` | `Timbratune AppStore` (`.mobileprovision`), base64 |
| `ASC_KEY_ID` | App Store Connect API key id |
| `ASC_ISSUER_ID` | Its issuer id |
| `ASC_P8` | The key's `.p8` file, as text or base64 |

Base64 of a file in PowerShell, copied to the clipboard:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\cert.p12")) | Set-Clipboard
```

The API key: App Store Connect › Users and Access › Integrations › App Store Connect API › Team
Keys, role **App Manager**. The `.p8` downloads only once. The app record must exist before the
first upload: App Store Connect › Apps › + › New App, bundle id `pl.eleanorakagner.timbratune`.

The profile expires a year after it was made, the certificate too: renew them in the developer
portal, then update the secrets.

## What the head does

- **Storage**: takes in `Documents/takes`, which the Files app shows (On My iPhone › Timbratune;
  `UIFileSharingEnabled`), so takes can be copied in and out like the Android app folder.
  Settings go in `Library/Application Support`.
- **Audio session**: play-and-record, playback on the loudspeaker, **measurement mode** (no
  automatic gain, noise suppression or echo cancellation on the analysed voice), preferred
  44.1 kHz. Bluetooth headset microphones (8–16 kHz) aren't offered.
- **Microphone permission**: asked at the first recording (`NSMicrophoneUsageDescription`).
- **Share sheet**: `UIActivityViewController` from `Library/Caches/share`.
- **miniaudio**: SoundFlow 1.4.1 ships it as `miniaudio.framework`, embedded and signed in the
  bundle's `Frameworks/` (`NativeReference`). SoundFlow's own resolver looks for it under
  `runtimes/ios-arm64/native/` in the app, where the App Store doesn't allow frameworks.
  `MiniaudioFramework.Prepare()` makes that path in `Library/Caches` as a link to the real
  framework and sets the base directory to it. Drop the shim if a later SoundFlow finds the
  framework itself.
- **Icon**: `Assets.xcassets/AppIcon.appiconset/icon-1024.png`, drawn by
  `scripts/make-ios-icon.py` (the Android icon's design at 1024 px, square and opaque).

## Not done yet

- **M4A/AAC import** (Voice Memos, most phone recordings): the managed WAV/AIFF and miniaudio
  MP3/FLAC decoders are all there is. An AVFoundation decoder provider (`AVAudioFile`), like
  `Audio.Android`'s MediaCodec one, would cover it.
- **Interruptions** (a call, Siri, another app taking the microphone): not handled yet; recording may
  stop without the app knowing.
- **Opening `.tmbr` files** from other apps (document types) isn't declared.
- **Simulator**: no miniaudio for it in SoundFlow, so devices only.
- Not measured on a phone yet: speed of the live analysis on iOS (Mono AOT), rendering (OpenGL
  ES, Avalonia's default; Metal is marked experimental).
