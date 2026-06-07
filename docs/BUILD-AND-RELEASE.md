# Lumos — Build & Release Guide

How to produce and ship a Lumos release. Lumos is distributed as a single-file
Windows installer via **Velopack**, published to **GitHub Releases**. Users get
auto-update from within the app (Phase 14).

---

## One-time setup (on the build machine)

1. **.NET 8 SDK** — verify with `dotnet --version` (should be `8.x`).
2. **Velopack CLI**:
   ```powershell
   dotnet tool install -g vpk
   # later, to update: dotnet tool update -g vpk
   ```
3. **EFF wordlist** present at `src\Lumos.Core\Resources\eff_large_wordlist.txt`
   (download from https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt).
   It's embedded into the build; without it the passphrase generator fails at
   runtime.

---

## Building a release

```powershell
.\build\release.ps1 -Version 1.0.0
```

What it does:
1. `dotnet publish` — self-contained, win-x64, **not** single-file. Self-contained
   means the target machine needs **no .NET install**. We don't use
   `PublishSingleFile` because it conflicts with the native SQLite encryption
   library (`e_sqlite3mc.dll`) — Velopack does the single-file packaging instead.
2. Verifies `e_sqlite3mc.dll` is in the publish output (encrypted vaults depend
   on it).
3. `vpk pack` — Velopack bundles the publish folder into:
   - `Setup.exe` — the single installer users download
   - `Lumos-{version}-full.nupkg` — the update package
   - `RELEASES` — the manifest the in-app updater reads

Output lands in `build\releases\`.

---

## Publishing to GitHub

1. Create a GitHub Release tagged **`v{version}`** (e.g. `v1.0.0`) — the tag must
   match the `-Version` you built.
2. Upload **all** files from `build\releases\` to that release:
   - `Setup.exe` (what users download and run)
   - the `.nupkg` and `RELEASES` files (the updater needs these to detect and
     download updates)

That's it. Users who already have Lumos installed will see the update on their
next in-app update check (Phase 14). New users download `Setup.exe`.

---

## Versioning

- Bump `<Version>` in `src\Lumos.Desktop\Lumos.Desktop.csproj` **and** pass the
  same value to `release.ps1 -Version`.
- Use semantic versions: `MAJOR.MINOR.PATCH`.
- The git tag (`vX.Y.Z`), the csproj `<Version>`, and the `-Version` argument
  must all agree, or the updater can get confused about what's newest.

---

## What the user experiences

- **First install:** download `Setup.exe` from the GitHub Release, run it. No
  .NET prerequisite. Lumos installs to `%LocalAppData%\Lumos` (per-user, no admin
  needed) and adds a Start-menu shortcut.
- **Their data:** the vault lives in `%APPDATA%\Lumos\` — completely separate from
  the install location. **Updates and uninstalls never touch the vault.**
- **Updates:** handled in Phase 14 (an in-app "update available" prompt).

---

## Important notes

- **Offline guarantee intact:** the only network call Lumos ever makes is the
  Velopack update check, and that only runs when the user triggers it (Phase 14).
  No telemetry, no backend, no background phone-home.
- **The vault is never bundled or shipped.** Only the application code is in the
  installer. User data stays in `%APPDATA%\Lumos\` on each machine.
- **.NET IL is decompilable** (ILSpy etc.). The installer ships compiled code, not
  source, but a determined person can read the logic. This matters for the
  product-key check (Phase 15) — documented there.
- **Code signing:** not configured. Unsigned installers trigger a Windows
  SmartScreen warning ("Unknown publisher") on first run. For a free GitHub-
  distributed tool this is normal; users click "More info -> Run anyway." A real
  code-signing certificate (~$100-400/yr) removes the warning if ever wanted —
  Velopack supports it via `--signParams`.
