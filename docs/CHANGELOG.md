# Lumos — Changelog

All notable changes, newest first.

---

## v2 direction (in progress) — "Offline-only, hardened, distributable"

A deliberate pivot. v1 had a full backend (OAuth sign-in, sessions, OTP, server MFA,
HIBP breach detection). v2 strips all of that to make Lumos a **100% offline application
that never makes a network call**, then adds attachments, a security-focused UI redesign,
a real installer with auto-update, and a product-key activation step.

Rationale: the smallest attack surface is the one that doesn't exist. An app that never
touches the network cannot leak data over the network.

### Phases
- **Phase 10** — DONE. Stripped the backend (see detail below). Pure offline app now.
- **Phase 11** — DONE. UI overhaul. New palette: black-dominant (`#0A0A0B`), neutral
  grey structure, **steel-blue `#4A7DBF` as the primary accent** (buttons, focus, active
  sidebar, glyphs), **gold reserved for the LUMOS wordmark + TOTP ring only**, blood-red
  `#D63A4A` for danger. Sharp edges everywhere (all corner-rounding → 0; FAB is now a
  square). Glow heavily reduced: removed card glow halos, the animated tron-line, CRT
  scanlines, corner glows, neon edge strips, and most DropShadow effects; the few that
  remain (wordmarks, TOTP ring) are softened. Backdrop is now flat near-black + a faint
  2.5% static grid. Added a global themed `ToolTip` style (the default Windows tooltip
  rendered pale-on-pale and was unreadable — now dark surface, light text, steel border).
- **Phase 12** — DONE. Document/file attachments. Schema v3 adds an `attachments`
  table (encrypted BLOBs inside the vault — SQLCipher encrypts them transparently). Any
  file type, 25 MB/file cap, all four entry types. Per-entry add via file picker, list
  with type glyphs + size, inline image preview, save-out, delete. Metadata and bytes are
  separate reads (listing never loads file bytes). Entry deletion explicitly removes its
  attachments (not relying on the FK cascade, which connection pooling can reset).
  `AttachmentRepository` + 12 tests in Core; `AttachmentsPanelViewModel` /
  `AttachmentItemViewModel` + a `BytesToImageConverter` + generic `RelayCommand<T>` in
  Desktop. Also fixed a latent generator bug: 4 EFF words are hyphenated (drop-down,
  felt-tip, t-shirt, yo-yo) and collided with the "-" separator — now stripped at load.
- **Phase 13** — DONE. Self-contained build + Velopack installer. Added the `Velopack`
  package and `VelopackApp.Build().Run()` bootstrap (first line of startup; no-op in
  dev). Desktop csproj set to self-contained win-x64 (no .NET install needed on target),
  deliberately NOT `PublishSingleFile` (conflicts with the native `e_sqlite3mc.dll` —
  Velopack does the single-file packaging instead). Added `build/release.ps1` (publish +
  `vpk pack` → `Setup.exe` + update packages) and `docs/BUILD-AND-RELEASE.md`. Output
  goes to GitHub Releases; users download one `Setup.exe`, installs per-user to
  `%LocalAppData%\Lumos`, vault stays in `%APPDATA%\Lumos` untouched by install/update.
  Verified 2026-06-04: `vpk pack` produced `Lumos-win-Setup.exe`; explicit `Program.Main`
  runs Velopack before WPF (clean build, 0 warnings, app launches normally).
- **Phase 14** — DONE (pending real GitHub URL). In-app update check, manual only — no
  automatic or background checks, preserving the "no network unless you ask" guarantee.
  `UpdateService` wraps Velopack's `UpdateManager` over a `GithubSource`; `UpdateViewModel`
  drives a "CHECK FOR UPDATES" button at the bottom of the sidebar (by LOCK) showing the
  current version. On a found update: a steel-blue "UPDATE TO vX" button + the reassurance
  "Your vault and all your data stay exactly as they are"; applying downloads and restarts
  into the new version. Dev builds report "updates apply to the installed app only".
  NotInstalledException catch removed). RepositoryUrl set to the real repo:
  https://github.com/Arrowh-0h/LUMOS (verified working in dev — the check correctly
  reports "updates apply to the installed app only" under dotnet run).
- **Phase 15** — DONE. Product-key activation gate. Format `LUMOS-XXXX-XXXX-XXXX` from an
  unambiguous base32 alphabet; offline HMAC partial-signature (8-char serial + 4-char
  truncated HMAC-SHA256 of the serial under a baked-in secret). `ProductKey` validator +
  13 tests in Core; private `tools/keygen` console app generates keys with the same
  secret. App blocks on first launch behind `ActivationView` until a valid key is entered;
  the key is stored DPAPI-encrypted (per-user) in `%APPDATA%\Lumos\license.dat` and
  re-validated each launch. Honest limitation per D-V2-07: the secret ships in
  decompilable IL, so a determined reverse-engineer can forge keys — this raises the bar
  and "feels official" but is not anti-piracy (the product is free). Verified 2026-06-06:
  212 Core tests passing; keygen produced a valid key (LUMOS-YKF9-... format); activation
  gate blocks first launch and remembers activation across relaunches.
- **Phase 16** — DONE. Security review + threat model. Added `docs/SECURITY.md` (technical
  threat model: key hierarchy, in-scope vs out-of-scope threats, on-disk file sensitivity,
  honest product-key and network-surface sections). Hardening pass: fixed stale v1
  references in `AesGcmCrypto` doc, added a clarifying header note to crash.log about not
  logging secrets. Audit findings: crypto sound (Argon2id wrapping + random cipher key +
  SQLCipher; AES-GCM with per-message nonce; constant-time compares; CSPRNG throughout),
  clipboard verified-clear is correct, FTS5 excludes secrets, no secret material reaches
  logs or exception messages. Documented (not "fixed", because they're inherent) the
  out-of-scope items: known master password, compromised OS/RAM scraping, decompilable IL
  vs the product key, unsigned installer.

### Phase 10 — Strip the backend (DONE)
**Deleted:** entire `src/Lumos.Backend/` (37 files) and `tests/Lumos.Backend.Tests/`
(10 files); `src/Lumos.Core/Backend/` (BackendDtos, LumosBackendClient, SessionStorage);
`src/Lumos.Core/Breach/` (BackendBreachChecker, IBreachChecker, VaultBreachScanner);
`src/Lumos.Desktop/Common/EmbeddedBackendHost.cs`; `AccountViewModel.cs`,
`MfaPanelViewModel.cs`, `AccountView.xaml(.cs)`; `BreachCheckerTests.cs`.

**Edited:** `Lumos.sln` (5 projects → 3); `App.xaml.cs` (removed all `#if DEBUG` backend
hosting; kept global exception handlers + crash logging); `MainWindow.xaml.cs` (removed
backend client + session storage wiring); `ShellViewModel.cs` (removed Account pane +
breach checker + backend params; ctor now: entries, folders, idleMonitor, systemEvents,
settings, fileDialogs); `VaultViewModel.cs` (removed all breach machinery; ctor now just
takes the entry repo); `EntryListItemViewModel.cs` + `EntryDetailViewModel.cs` (removed
breach properties); `ShellView.xaml` (removed ACCOUNT sidebar item + content area);
`VaultView.xaml` (removed BREACHED badge + breach banner); `Lumos.Desktop.csproj`
(removed backend ProjectReference + ASP.NET FrameworkReference).

**Kept:** entry-level TOTP (client-side RFC 6238, fully offline).

**Test count:** 240 → **184 Core tests, all passing** (confirmed 2026-06-03). Removed 51
backend tests + breach-checker tests. Verified offline: `dotnet build | Select-String
"AspNetCore"` returns nothing — no ASP.NET Core / network stack linked into the app.

---

## v1 (complete) — "Full-featured password manager with backend"

240 tests passing (189 Core + 51 Backend). Shipped every roadmap phase. Most of the
backend half is removed in v2; recorded here for history.

**Phases 1–6 (desktop foundation):** Argon2id KDF (64MB/3 iters), AES-256-GCM, SQLCipher
v4 vault at `%APPDATA%\Lumos\vault.db` + sidecar header. Exponential unlock backoff,
opt-in self-destruct. Four entry types (Login/SecureNote/Card/Identity), FTS5 search that
never indexes secrets. Folders + tags. Password generator (random + EFF passphrase).
Auto-lock (idle/sleep/screen-lock/minimize/manual), clipboard auto-clear. Frameless
cyberpunk UI.

**Backend Slices 1–5 (removed in v2):** ASP.NET Core API at localhost:5050 in DEBUG;
OAuth sign-in + sessions; email/SMS OTP; HIBP breach detection with k-anonymity; account
TOTP MFA + recovery codes.

**Phase 7 (kept):** entry TOTP with countdown ring.
**Phase 9:** export/import (LXP1 encrypted, Lumos JSON, Bitwarden JSON, CSV) with merge
preview.

**Notable v1 bug fixes:** global exception handler added (crashes were silent); WPF
Color-into-Brush fix in Controls.xaml; sys:String ConverterParameter NRE → direct bool
binding; EFF wordlist is a required external file.
