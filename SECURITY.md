# Lumos — Security & Threat Model

Audience: developers and security-minded users. This document states precisely
what Lumos protects, how, and — just as importantly — what it does **not**
protect against. We prefer honest limits over reassuring vagueness.

Lumos is a **local-first, offline, zero-knowledge** Windows password manager.
There is no backend, no account, no telemetry, and no network activity except a
single user-initiated update check (see §6).

---

## 1. Cryptographic design

### Key hierarchy
```
master password (never stored)
      │  Argon2id  (m = 64 MB, t = 3, p = 4, 16-byte random salt)
      ▼
wrapping key (32 bytes, RAM only)
      │  AES-256-GCM  (unwraps the envelope in vault.db.header.json)
      ▼
cipher key (32 bytes, random per vault, fixed for the vault's life)
      │  SQLCipher v4  (AES-256)
      ▼
vault.db  (entries, attachments — all encrypted at rest)
```

- **Argon2id** derives a wrapping key from the master password. Parameters are
  stored in the header so they can be upgraded in future versions without
  breaking existing vaults.
- The **cipher key** that actually encrypts the database is random and never
  derived from the password directly. The password-derived key only *wraps*
  (encrypts) the cipher key in the header envelope (AES-256-GCM).
- **Password change** re-wraps the cipher key under a new wrapping key; the
  database itself is not re-encrypted. (This is also a deliberate workaround for
  SQLite3MC's rekey path, which would otherwise re-run its own KDF on a raw key.)
- **Vault contents** are encrypted by **SQLCipher v4** (AES-256). Attachments
  are BLOBs inside the same database, so they inherit the same encryption.

### Primitives
- KDF: Argon2id, 64 MB / 3 iterations / parallelism 4, 16-byte salt, 32-byte output.
- Vault cipher: SQLCipher v4 (AES-256-CBC + HMAC per SQLCipher defaults).
- Auxiliary cipher (exports): AES-256-GCM, fresh 12-byte nonce per message,
  16-byte tag. (`AesGcmCrypto`.)
- RNG: `RandomNumberGenerator` (OS CSPRNG) everywhere randomness is needed.
- Constant-time comparisons (`CryptographicOperations.FixedTimeEquals`) for
  MAC/signature checks.

---

## 2. What Lumos protects against (in scope)

| Threat | Mitigation |
|---|---|
| Theft of the vault file at rest (laptop stolen, backup leaked, cloud-synced file exposed) | Vault is SQLCipher-AES-256 encrypted. Without the master password, the file is opaque. The header envelope only yields the cipher key to someone who can derive the wrapping key, which requires the password + Argon2id work. |
| Brute-force / dictionary attack on the master password | Argon2id (64 MB, t=3) makes each guess expensive in time and memory. Plus an in-app unlock backoff (exponential delay after failed attempts). |
| Tampering with the vault or its header | AES-GCM on the envelope and SQLCipher's per-page HMAC detect modification; a tampered file fails to open rather than yielding wrong data. |
| Secrets leaking into the search index | FTS5 index excludes secret fields (passwords, card numbers, CVVs, TOTP secrets) by design. |
| Clipboard lingering after a copy | Copied secrets auto-clear after a timeout, with a *verified* clear (only clears if the clipboard still holds what Lumos wrote — never wipes something you copied afterward). |
| Network exfiltration | There is no network code paths except the user-initiated update check (§6). The app cannot phone home because the capability does not exist. |
| Idle/unattended machine | Auto-lock on idle, sleep, screen-lock, and manual lock; keys are dropped from RAM on lock. |

---

## 3. What Lumos does NOT protect against (out of scope)

These are stated plainly. None of them have a userspace fix; pretending otherwise
would be dishonest.

- **An attacker who knows your master password.** The entire model rests on the
  password being secret and strong. Lumos cannot recover it and cannot
  distinguish you from anyone else who types it.
- **A compromised operating system / kernel-level malware.** While the vault is
  unlocked, the cipher key and decrypted fields exist in RAM. A keylogger,
  memory scraper, or malicious driver running with sufficient privilege can read
  the password as you type it or scrape plaintext from memory. No userspace app
  can defend against an attacker who owns the OS.
- **Cold-boot / RAM remanence attacks** while the vault is unlocked. Keys live in
  managed memory; we zero buffers we control (`SecureMemory.Zero`), but the .NET
  GC may copy or retain managed strings, and we cannot guarantee every byte is
  scrubbed instantly. Locking the vault and closing the app is the mitigation.
- **Malicious or backdoored dependencies / supply chain.** Lumos trusts SQLCipher
  (SQLite3MC), Argon2 bindings, Velopack, and the .NET runtime. A compromise in
  any of those is out of scope.
- **Shoulder-surfing, screen capture, physical observation.**
- **A determined reverse-engineer defeating the product key.** See §5.

---

## 4. Sensitive files on disk

All under `%APPDATA%\Lumos\`:

| File | Contents | Protection |
|---|---|---|
| `vault.db` | All entries + attachments | SQLCipher AES-256 (needs master password) |
| `vault.db.header.json` | KDF params + wrapped cipher key | The cipher key inside is AES-GCM-wrapped; useless without the password-derived key. Must travel WITH `vault.db` for the vault to open. |
| `license.dat` | The activated product key | Windows DPAPI (per-user). See §5. |
| `crash.log` | Exception type/message/stack traces | **Plaintext.** By design Lumos never puts secret material into exception messages, so this should not contain secrets — but it can reveal file paths and internals. Safe to delete anytime. |

The vault key is **never** written to disk in any form that isn't wrapped by the
password-derived key. The master password is never persisted.

---

## 5. Product key / activation (honest limitations)

Lumos is free. The product key is a "feels official" activation gate, **not** a
security or anti-piracy control. Details in `docs/LICENSING.md`.

- Keys are `LUMOS-XXXX-XXXX-XXXX`: an 8-char serial plus a 4-char truncated
  HMAC-SHA256 signature over the serial, under a secret baked into the app.
- **The secret ships inside the application, and .NET IL decompiles cleanly**
  (ILSpy, dnSpy). A determined person can extract the secret and forge keys.
  This scheme resists casual guessing and behaves like a real product key, but
  it is explicitly NOT unbreakable. We document this rather than imply strength
  it doesn't have.
- Activation is stored in `license.dat` via **DPAPI (per-user)**. It is checked
  *before* the vault is unlocked, so it cannot use the master-password-derived
  key — DPAPI ties it to the Windows user account instead. Copying `license.dat`
  to another user won't work; the stored key is re-validated on every launch.

The product key has **no bearing on vault security**. Bypassing activation does
not weaken the vault's encryption in any way; they are independent.

---

## 6. Network surface

Lumos makes **exactly one** kind of network request, and only when the user
clicks "Check for updates":

- Velopack queries the configured GitHub Releases endpoint
  (`https://github.com/Arrowh-0h/LUMOS`) for a newer version.
- There is no automatic, scheduled, or background checking. No check happens on
  launch. No data about the user or vault is transmitted — it is a read of a
  public releases feed.
- Updates replace application files only. The vault in `%APPDATA%\Lumos` is never
  touched by install, update, or uninstall.

If you never click the button, Lumos makes no network connections at all.

---

## 7. Distribution & integrity

- Distributed as a Velopack installer via GitHub Releases (self-contained, no
  .NET prerequisite on the target).
- **The installer is not code-signed.** Windows SmartScreen will warn "Unknown
  publisher" on first run. This is expected for a free, unsigned, GitHub-hosted
  tool. Users who want assurance can build from source. A code-signing
  certificate would remove the warning but is not currently used.
- Because builds are unsigned, the strongest integrity guarantee is "build it
  yourself from source." Released binaries rely on GitHub's transport security
  and your trust in the release author.

---

## 8. Reporting a vulnerability

This is a small, free, open project. If you find a security issue, open an issue
on the GitHub repository (or contact the maintainer directly for anything you
consider sensitive). There is no bug-bounty program.

---

## 9. Summary

Lumos gives you strong, standard, offline encryption of your vault (Argon2id +
AES-256 via SQLCipher), a minimal attack surface (no network, no backend), and
honest handling of secrets in memory and on the clipboard. Its guarantees hold
against a stolen vault file and an offline attacker. They do **not** hold against
a compromised OS, a known master password, or a determined reverse-engineer
targeting the (cosmetic) product key. Those limits are inherent, not oversights.
