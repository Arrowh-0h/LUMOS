# Lumos — Decisions Log

Locked decisions and why. When a decision changes we add a superseding entry rather than
deleting, so the reasoning history stays intact.

## v2 decisions

**D-V2-01 — Offline only; remove the backend entirely — LOCKED**
No network code except the explicit, user-initiated update check (Phase 14).
Why: smallest attack surface is the one that doesn't exist; login was the biggest
data-collection surface.

**D-V2-02 — Master password is the only credential — LOCKED**
No accounts, no OAuth, no sign-in.

**D-V2-03 — Drop breach detection — LOCKED**
It needed a backend or a network call; both conflict with offline-only. User chose drop.

**D-V2-04 — Keep entry-level TOTP — LOCKED**
Purely client-side RFC 6238, no network. High value, zero cost to the offline guarantee.

**D-V2-05 — Installer: Velopack — LOCKED**
Free, GitHub-Releases-native, built-in update flow, single-file output. Over WiX/MSI.
Phase 13: publish self-contained win-x64 but NOT PublishSingleFile (conflicts with native
e_sqlite3mc.dll); Velopack packages the publish folder into Setup.exe + update packages.
Per-user install to %LocalAppData%\Lumos (no admin). Velopack 0.0.1298; vpk CLI as global
dotnet tool. Build via build/release.ps1; see docs/BUILD-AND-RELEASE.md.

**D-V2-06 — Distribution: GitHub Releases, free, exe-only — LOCKED**
Publish only the compiled installer. Source stays private. Note: .NET IL decompiles
cleanly (ILSpy), so "source private" is not "logic secret" — relevant to D-V2-07.

**D-V2-07 — Product key is "feels official," not anti-piracy — LOCKED**
User emails for a key; entering it activates for life; stored encrypted locally; hashed
check baked into the build and obfuscated. ACCEPTED LIMITATION: any client-side license
check is bypassable by a determined reverse-engineer. Fine for a free product; documented
rather than hidden.
Phase 15 detail: format LUMOS-XXXX-XXXX-XXXX; offline HMAC partial-signature (8-char
serial + 4-char truncated HMAC-SHA256 under a baked-in secret). Blocks on first launch
until valid; stored DPAPI-encrypted per-user at %APPDATA%\Lumos\license.dat, re-validated
each launch. Keys issued via private tools/keygen. Secret is a single constant — change it
to retire a batch. Full writeup in docs/LICENSING.md.

**D-V2-08 — UI: black-dominant, sharp, low-glow — LOCKED (details in Phase 11)**
Palette: black (dominant) + red + grey + blue + gold accents. Drop cyan. Sharp edges,
minimal/no rounding. Reduce neon glow. Proper alignment. Real tooltips on window controls.

**D-V2-09 — Attachments stored encrypted inside the vault — LOCKED (Phase 12)**
Multiple per entry, image preview, ~10MB/file cap, stored as BLOBs in the SQLCipher DB so
they inherit the vault's encryption and travel with backups automatically.

**D-V2-10 — Vault file unreadable without the master password — LOCKED (guarantee)**
A third party who obtains vault.db cannot read it. Argon2id + AES-256-GCM via SQLCipher;
master password never stored; key only in RAM while unlocked.
Documented caveats (Phase 16 threat model): (a) attacker with the master password wins;
(b) while unlocked, keys/plaintext live in RAM and a kernel-level attacker could scrape
them; (c) copied passwords briefly sit in the clipboard (auto-cleared). No userspace app
can defeat an attacker who owns the kernel — stated rather than overpromised.

## v1 decisions still in force
- **D-01** Core has no UI dependencies (testability + reuse).
- **D-02** Master password never persisted; key only in RAM while unlocked.
- **D-03** Sensitive fields (passwords, CVVs, TOTP secrets) never indexed in FTS5.
- **D-04** Argon2id m=64MB / t=3 / p=1.
- **D-05** Auto-lock defaults: idle/sleep/screen-lock/manual ON; lock-on-minimize OFF.

## v1 decisions SUPERSEDED by v2
- D-OLD-01 Backend in DEBUG via EmbeddedBackendHost — SUPERSEDED by D-V2-01.
- D-OLD-02 Vault-encrypted 7-day sessions — SUPERSEDED by D-V2-02.
- D-OLD-03 HIBP breach detection with k-anonymity — SUPERSEDED by D-V2-03.
- D-OLD-04 Server-side account MFA + recovery codes — SUPERSEDED by D-V2-02.
