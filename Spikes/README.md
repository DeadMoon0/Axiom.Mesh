# Spikes

Throwaway code that answers the open questions in [DESIGN.md §15](../Documentation/DESIGN.md#15-open-questions).
Nothing here ships, nothing here is referenced by a shipped project, and a spike is deleted once its answer is
recorded in the design. The spikes build against the published Axiom packages from nuget.org (see `NuGet.config`),
so they test what Mesh would consume, not a checkout.

| Spike | Question | Run |
|---|---|---|
| `OutboundAdoption` | 15.1: can LWSE Core serve a connection this node dialled, with admission, TLS, observers and drain? Also runs the LDB check on the desktop. | `dotnet run -c Release --project Spikes/OutboundAdoption` |
| `Android` | 15.2: do LWSE and LDB build with full trimming for, and run on, Android? Runs the same scenarios as `OutboundAdoption` on the phone. | build below, then install the APK and read logcat tag `AxiomMesh` |

`Shared/` holds the scenarios both spikes run: a symmetric `mesh-spike/1` protocol described with ProtoStream, a
mutual TLS 1.3 layer pinned to public keys, and three nodes on loopback (A and B pin each other, nobody pins Eve).

## Android

The Android SDK and JDK that Visual Studio installs are not on the path; state them:

```bash
dotnet build Spikes/Android -c Release -p:AndroidSdkDirectory="C:\Program Files (x86)\Android\android-sdk" -p:JavaSdkDirectory="C:\Program Files\Android\openjdk\jdk-21.0.8"
```

The Visual Studio SDK is not writable, so the emulator lives in a second, user-owned SDK at
`%LOCALAPPDATA%\Android\Sdk` (emulator, platform-tools, platform 35, build-tools 36, system image
`android-35;google_apis;x86_64`, AVD `mesh35`). Boot it headless, then install, launch and read the log:

```bash
"%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe" -avd mesh35 -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect
```

```bash
"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe" install -r Spikes/Android/bin/Release/net10.0-android/de.axiom.mesh.spike-Signed.apk
```

```bash
"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe" shell monkey -p de.axiom.mesh.spike -c android.intent.category.LAUNCHER 1
```

```bash
"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe" logcat -s AxiomMesh
```

The app shows the same log on screen and ends with one `PASS` or `FAIL` line per question. A phone in USB
debugging mode works the same way. Result on 2026-10-28, Android 15 emulator: every check passes.
