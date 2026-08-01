# Lumos — Build & Release Guide

How to produce and ship a Lumos release. Lumos is distributed as a single-file
Windows installer via **Velopack**. The **x64** build goes to GitHub Releases and
updates itself in-app; the **ARM64** build is distributed from the website and
updates by re-download. The reason for that split is in "Two architectures,
one feed" below — read it before changing anything about distribution.

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

## Two architectures, one feed — why ARM64 is handled separately

Velopack's default channel is per-OS (`win`), **not** per-architecture. If both
the x64 and ARM64 builds are published to that same default feed, they overwrite
each other's release manifest, and an ARM64 install polling it will be offered
the x64 package — then replace itself with a build for the wrong CPU.

The correct long-term fix is per-architecture channels (`--channel win-x64`,
`--channel win-arm64`), plus a one-time migration for existing users who are
already on the default `win` channel. That work has not been done yet.

Until it is, the arrangement is:

| Build | Distributed via | Updates |
|---|---|---|
| **x64** | GitHub Releases, default channel | In-app update check, as normal |
| **ARM64** | The website (`lumos-site/public/`) | User re-downloads and reinstalls |

`UpdateService.UpdatesAvailableForThisBuild` returns `false` on ARM64, so the
in-app check is **blocked in code** — it never contacts the network and tells the
user to visit the website instead. This is enforced rather than merely documented
because the failure mode is a broken installation from a single click.

Note that the **x64 installer runs on ARM64 machines** through Windows' emulation.
The native ARM64 build is a performance improvement, not a compatibility
requirement — so if this arrangement ever becomes a problem, shipping x64 only is
a valid fallback.

---

## Building a release

Bump `<Version>` in `src\Lumos.Desktop\Lumos.Desktop.csproj` first, then:

```powershell
# Always run the tests before packing anything.
dotnet build
dotnet test
```

### x64 (goes to GitHub)

```powershell
dotnet publish src\Lumos.Desktop -c Release -r win-x64 -o build\publish\win-x64

dotnet vpk pack --packId Lumos --packVersion 2.0.0 `
  --packDir build\publish\win-x64 `
  --mainExe Lumos.exe `
  --outputDir build\releases\win-x64 `
  --icon src\Lumos.Desktop\lumos.ico
```

**No `--channel`.** x64 stays on the default feed so existing installs continue
to see updates.

### ARM64 (goes to the website)

```powershell
dotnet publish src\Lumos.Desktop -c Release -r win-arm64 -o build\publish\win-arm64

dotnet vpk pack --packId Lumos --packVersion 2.0.0 `
  --packDir build\publish\win-arm64 `
  --mainExe Lumos.exe `
  --runtime win-arm64 `
  --outputDir build\releases\win-arm64 `
  --icon src\Lumos.Desktop\lumos.ico
```

`--runtime win-arm64` is required, or Velopack generates an x64 installer stub.

### Notes on the publish step

- **Self-contained**, so the target machine needs no .NET install. Set in the
  csproj, not on the command line.
- **Not** `PublishSingleFile` — it conflicts with the native SQLite encryption
  library (`e_sqlite3mc.dll`). Velopack does the single-file packaging instead.
- `RuntimeIdentifiers` declares `win-x64;win-arm64`; a conditional default keeps
  a plain `dotnet build` working without naming a RID.
- **Verify `e_sqlite3mc.dll` is in each publish folder.** Encrypted vaults cannot
  open without it, and a missing copy is the single most likely cause of a
  first-run crash on a user's machine. The app's own startup self-test now checks
  this at runtime and writes the result to `crash.log`.

---

## Publishing to GitHub (x64 only)

1. Create a GitHub Release tagged **`v{version}`** — the tag, the csproj
   `<Version>`, and `--packVersion` must all agree, or the updater gets confused
   about what is newest.
2. Upload **all three** files from `build\releases\win-x64\`:
   - `Setup.exe` — what users download and run
   - `Lumos-{version}-full.nupkg` — the update package
   - the release manifest (`RELEASES` / `releases.win.json`)

   **Do not skip the manifest.** v1.0.0 shipped without it and the in-app updater
   silently reported "up to date" forever. If only source archives are attached,
   the release is broken.
3. **Do not attach the ARM64 build.**
4. **Check the installer's filename** against `downloadUrl` in the website's
   `src/config.js` (`Lumos-win-Setup.exe`). If Velopack emits a different name,
   either rename the asset or update the constant — otherwise the website's main
   download button 404s.

---

## Publishing ARM64 (website)

1. Copy `Setup.exe` from `build\releases\win-arm64\` into the site's public
   folder, named exactly:
   ```
   lumos-site\public\Lumos-win-arm64-Setup.exe
   ```
   That path must match `downloadArmUrl` in `src/config.js`.
2. Rebuild and redeploy the site (`npm run build`, deploy `dist\`).

---

## Superseding an old release

When a release becomes unusable rather than merely outdated, edit its notes to say
so — don't delete it, which breaks download history. v1.0.0 is the example: it
blocks on the product-key activation gate, and the key list was removed from the
repo in v2, so anyone installing it now has an app they cannot open.

---

## What the user experiences

- **First install:** download `Setup.exe`, run it. No .NET prerequisite. Lumos
  installs to `%LocalAppData%\Lumos` (per-user, no admin needed) and adds a
  Start-menu shortcut.
- **Their data:** the vault lives in `%APPDATA%\Lumos\` — completely separate from
  the install location. **Updates and uninstalls never touch the vault.**
- **On first unlock after updating from v1**, existing users are offered a
  recovery code and their vault header is upgraded to format v3. See
  `docs/RECOVERY.md`. This is the highest-risk path in any release; test it by
  hand against a real v1 vault before shipping.

---

## Important notes

- **Offline guarantee intact:** the only network call Lumos ever makes is the
  update check, and only when the user triggers it. No telemetry, no backend, no
  background phone-home.
- **The vault is never bundled or shipped.** Only application code is in the
  installer.
- **KDF parameters are per-vault.** Raising the Argon2id defaults affects only
  newly created vaults and vaults whose password is changed afterwards. Existing
  vaults keep deriving with the parameters recorded in their own header, so
  nobody is locked out — and nobody gets the benefit either without a password
  change.
- **`SQLitePCLRaw.bundle_e_sqlite3mc` is pinned at 2.1.11 deliberately.** That
  package was **removed entirely in SQLitePCLRaw v3.x**. A routine "update all
  packages" would silently strip out the encryption provider. Do not bump it
  without checking what replaced it.
- **Code signing:** not configured. Unsigned installers trigger a Windows
  SmartScreen warning ("Unknown publisher") on first run. For a free
  GitHub-distributed tool this is normal; users click "More info -> Run anyway."
  A code-signing certificate (~$100-400/yr) removes the warning — Velopack
  supports it via `--signParams`.
