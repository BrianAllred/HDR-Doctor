# HDR Doctor

A desktop app that inspects a HewDraw Remix installation and explains what is wrong
with it. Point it at an SD card folder (either an emulator's `sdmc` or `sdcard` folder, a real Switch SD card over Hekate or inserted in the PC, or a Switch SD over FTP) to get a report on any issues. Note that automatic repairs aren't available over FTP.

This is an **unofficial** app. It is **not** a replacement for the HDR launcher.

<img width="1108" height="812" alt="image" src="https://github.com/user-attachments/assets/125c02d7-ff72-4cf1-9e39-e38a97b9e29c" />

<img width="1108" height="812" alt="image" src="https://github.com/user-attachments/assets/8d9a4816-f66e-423a-9d68-c3ef3924ea2b" />

<img width="1108" height="812" alt="image" src="https://github.com/user-attachments/assets/6d44acfa-5b9b-41f5-a99f-baf1b4d3b7b8" />

## What it checks

| Area | Looks for |
| ---- | --------- |
| Skyline plugins | Required plugins present; conflicting ones (`libparam_config.nro`, a stale `libhdr.nro`, stale `liblocal_latency_slider.nro`) |
| HID module | A leftover HID-HDR system-module patch under title `0100000000000013`, which breaks booting on more recent Switch firmware |
| HDR mod folders | The folders HDR owns are present and complete enough to boot; the versions they declare; other installed mods classified as code / gameplay / cosmetic |
| File verification | Every file MD5-verified against the release's published `content_hashes.json`; missing, altered and unexpected files. A separate step you start yourself — see below |
| Stage alts | `ultimate/stage-alts/Hashes_all` — missing or malformed, which panics the game during boot |
| Emulator config | RNG seed, VSync, graphics backend, async shaders and presentation, forced clocks, memory layout (yuzu family); PPTC (Ryujinx) |
| Launcher config | The desktop launcher's emulator and SD paths, including the case where it has been installing somewhere the emulator doesn't read |
| Crash/Skyline log | A Skyline or emulator log, matched against the messages that identify a known cause |
| Crash reports | On a Switch, the newest `atmosphere/crash_reports` from a Smash crash |

**RNG seed.** Skyline mods detect whether they are on a real Switch or an emulator by
where the game's code was loaded in memory. Enabling a fixed RNG seed is what stops
an emulator randomizing that address. With it off, HDR runs its Switch code, which breaks emulators.

## File verification

Checking the install against the release's published hashes means reading every HDR file.
That is fast off an SSD and slow off a real SD card or an FTP link, so it is not part of
a scan: press **Verify files** to trigger it.

Verification needs a published file list, so it is unavailable for private and
developer builds, and offline. The report says which of those it hit.

## Installing

Every tagged release publishes a self-contained build for each platform on the
[releases page](https://github.com/BrianAllred/HDR-Doctor/releases).

| Download | For |
| -------- | --- |
| `HDR-Doctor-x86_64.AppImage` | Linux. Holds the app and the console version both |
| `hdr-doctor-win-x64.exe` | Windows |
| `hdr-doctor-osx-arm64` / `hdr-doctor-osx-x64` | macOS, Apple Silicon and Intel |
| `hdr-doctor-linux-x64` | Linux, if you would rather not use the AppImage |
| `hdr-doctor-cli-*` | The console version on its own |
| `SHA256SUMS` | Checksums for all of the above |

The AppImage runs the app when you launch it, and the console version behind a flag:

```sh
chmod +x HDR-Doctor-x86_64.AppImage
./HDR-Doctor-x86_64.AppImage                          # the app
./HDR-Doctor-x86_64.AppImage --cli /path/to/sdmc      # the report on stdout
```

**macOS.** The builds are ad-hoc signed, which is enough to stop macOS calling them
damaged, but they are not notarized — so the first launch is refused as coming from an
unidentified developer. Right-click the file and choose **Open** to get the prompt that
lets you through, or clear the quarantine flag yourself:

```sh
xattr -dr com.apple.quarantine hdr-doctor-osx-arm64
chmod +x hdr-doctor-osx-arm64
```

**Linux and macOS** downloads from a browser arrive without the executable bit;
`chmod +x` is needed once.

## Updating

On launch the app asks GitHub whether a newer version exists and shows a banner if so.
Click **Install** to install the update. The console version does the same thing
on request:

```sh
hdr-doctor-cli --update
```

The download is checked against the release's published SHA-256 before it replaces
anything. Mismatches are ignored.

## Running it

```sh
dotnet run --project src/HdrDoctor.App
dotnet run --project src/HdrDoctor.App -- /path/to/sdmc --scan
dotnet run --project src/HdrDoctor.App -- /path/to/sdmc --scan --verify
```

The second form selects (or creates) a profile for that folder and scans immediately,
which is handy when you are talking somebody through it remotely. The third adds the
file verification step.

There is also a console version that prints the same report to stdout:

```sh
dotnet run --project tools/HdrDoctor.Cli -- /path/to/sdmc
dotnet run --project tools/HdrDoctor.Cli -- /path/to/sdmc --verify
```

## Profiles

Installs are saved as named profiles in
`$XDG_CONFIG_HOME/hdr-doctor/profiles.json` (Linux) or `%APPDATA%/hdr-doctor/profiles.json` (Windows) and
picked from the toolbar. On first run the app offers whatever emulators it finds
already installed.

A profile whose folder is not currently there shows as *unavailable* rather than
disappearing — that is what a card reader with no card in it should look like.

Each profile records whether it is a Switch or an emulator, because several checks
depend on it. The app suggests a value from the folder's shape; you can correct it
from the ⋯ menu.

## Fixes

The scan only reports. Findings that have a safe fix get a **Fix** checkbox, and
"Apply selected fixes" shows exactly what will happen before anything is touched.
Destructive steps are listed first and canceling is the default button.

**Reinstall HDR from scratch** deletes `ultimate/mods/{hdr,hdr-assets,hdr-stages}`
and installs the current release over the top. Other mods and non-HDR plugins are left alone.

Over FTP, fixes are not merely hidden: `FtpSource` does not implement
`IMutableInstallSource`, and remediations require it, so a fix over FTP does not
compile. Writing to the atmosphere folder while the Switch's operating system is booted
is not reliably consistent.

## Layout

| Component           | Purpose                        |
| ------------------- | ------------------------------ |
| src/HdrDoctor.Core  | all logic; no UI dependencies  |
| src/HdrDoctor.App   | Avalonia desktop UI            |
| src/HdrDoctor.Tests | xUnit tests                    |
| tools/HdrDoctor.Cli | console runner                 |

Everything worth testing lives in `Core`, so a check can be exercised without a
window. `IInstallSource` is the seam that lets the same check run unchanged against
a local folder and a remote Switch.

## Tests

```sh
dotnet test
```

Tests build real directory trees under the system temp folder rather than mocking a
filesystem.

## Where the rules come from

- `HewDraw-Remix/src/lib.rs` — `quick_validate_install()`, HDR's own boot-time check
- `HewDraw-Remix/scripts/full_package.py` — the authoritative SD-card layout
- `hdr-launcher-react/src/renderer/operations/verify.ts` — the existing hash verify
  and its ignore lists, ported so the two tools agree about a clean install
- `stage-alts-2/src/search.rs` — the unwrapping read of `Hashes_all`
- `ARCropolis/crates/config` — where ARCropolis keeps its settings
- `Atmosphere/stratosphere/creport/source/creport_crash_report.cpp` — the crash
  report's file name and layout
- Lots and lots (and lots) of hours in the HDR Discord server's #troubleshooting channel

## Disclaimer

This is an unofficial tool, not necessarily supported or endorsed by the HDR development team.
As always, huge thanks to them for their work.
