# HDR Doctor

A desktop app that inspects a HewDraw Remix installation and explains what is wrong
with it. Point it at an SD card folder (either an emulator's `sdmc` or `sdcard` folder, a real Switch SD card over Hekate or inserted in the PC, or a Switch SD over FTP) to get a report on any issues. Note that automatic repairs aren't available over FTP.

It is not a replacement for the HDR launcher.

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
