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

**D-V2-07 — Product key is "feels official," not anti-piracy — SUPERSEDED by D-V2-14 (v2)**
Kept on record rather than deleted: it happened, and the reasoning is worth remembering.
User emailed for a key; entering it activated for life; stored DPAPI-encrypted per-user at
%APPDATA%\Lumos\license.dat; format LUMOS-XXXX-XXXX-XXXX (8-char serial + 4-char truncated
HMAC-SHA256 under a baked-in secret). ACCEPTED LIMITATION at the time: any client-side
license check is bypassable by a determined reverse-engineer.
WHY IT WAS REMOVED: for a free, MIT-licensed, open-source app the gate bought nothing and
cost real friction — it blocked first launch, required the author to hand out keys by
email, and sat awkwardly against the project's own claim to be FOSS. Community feedback
(GitHub user GNUthulu) raised the same tension. The gate had no bearing on vault security
either way, so removing it weakened nothing.

**D-V2-14 — Offline recovery codes, no escrow — LOCKED (v2)**
Replaces D-V2-07's slot in the codebase (the activation gate came out; recovery went in).
A generated 30-character code (~147 bits) wraps the same cipher key the master password
wraps, in a second envelope with its own salt and Argon2id parameters. Either secret opens
the vault; neither produces the other.
REJECTED ALTERNATIVES, and why: stored facial identity (the template and the matching code
both sit on the attacker's disk, so the vault becomes decryptable without the password);
user-chosen word + passphrase (structurally right, but far lower entropy than a master
password, so it becomes the cheapest way in); developer-issued unlock token (either useless,
if it cannot carry key material, or a backdoor into every vault if it can).
ACCEPTED CONSEQUENCE: lose both the master password and the recovery code and the data is
unrecoverable by anyone, including the authors. Stated plainly to users rather than softened.
Full writeup in docs/RECOVERY.md.

**D-V2-15 — Reject forced periodic master-password rotation — LOCKED (v2)**
Considered for v2 and dropped. NIST SP 800-63B recommends against scheduled rotation, and
Microsoft removed it from the Windows baselines, for the same reason: people respond by
incrementing a digit and writing it down. Applying that to the single credential the whole
vault depends on would make it weaker, not stronger. Rotation on signal (suspected
compromise, lost device) remains the right trigger, and password change is now instant
because only the wrapped key is rewritten.

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
