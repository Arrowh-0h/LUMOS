# Lumos — Password Manager Specification

**Version:** 1.0 (Test Phase)
**Last updated:** 2026-05-16
**Team:** 2 members
**Status:** Internal use / pre-release

> *"Lumos" — to illuminate. A password manager that reveals your credentials when you need them, and seals them away when you don't.*

---

## 1. Product Overview

Lumos is a local-first, zero-knowledge password manager for Windows. It stores passwords, secure notes, payment cards, identities, and TOTP secrets in an AES-256 encrypted SQLite database that lives on the user's machine. A small backend service handles only what cannot be done offline: OAuth identity, OTP delivery, breach detection, and phone number validation.

**Design pillars**
1. **Zero-knowledge** — the master password and derived keys never leave the device.
2. **Local-first** — the vault is a file on disk. The user controls it.
3. **Trust-first UX** — security indicators, audit log, no telemetry, no dark patterns.
4. **Thematic delight** — modern cybersecurity aesthetic with restrained Harry Potter accents.

---

## 2. Tech Stack

| Layer | Choice |
|---|---|
| Desktop framework | WPF on .NET 8 (LTS) |
| Desktop language | C# |
| Local storage | SQLite3MC (SQLite + AES-256 transparent encryption, SQLCipher v4 format) |
| Vault location | `%APPDATA%\Lumos\vault.db` (configurable) |
| Backend | ASP.NET Core minimal API (C#) |
| Backend storage | SQLite |
| Backend hosting | Local dev for now; production host TBD |
| Crypto library | `Konscious.Security.Cryptography` (Argon2id), `System.Security.Cryptography` (AES-GCM), `SQLitePCLRaw.bundle_e_sqlite3mc` (encrypted SQLite) |
| Password strength | `zxcvbn-cs` |
| Logging | Serilog (structured) |
| TOTP | `Otp.NET` |
| QR codes | `QRCoder` |

---

## 3. Cryptography

### 3.1 Master password → encryption key

- **KDF:** Argon2id
- **Parameters (v1):** memory = 64 MB, iterations = 3, parallelism = 4
- **Salt:** 16 bytes, random per vault, stored in vault header
- **Output:** 32-byte key for SQLCipher
- KDF parameters are stored in the vault header so future versions can upgrade without breaking old vaults.

### 3.2 At-rest encryption

- **SQLite3MC** with the SQLCipher v4 cipher mode (`PRAGMA cipher='sqlcipher'`): AES-256-CBC + HMAC-SHA512 per-page MAC.
- Selected over Zetetic's official SQLCipher because the latter is commercial paid software, and the legacy free `bundle_e_sqlcipher` package is deprecated and known to silently produce unencrypted databases.
- No additional payload encryption inside rows — full-database encryption is sufficient.
- A runtime check verifies the encryption layer is actually loaded; vault creation refuses to proceed otherwise.

### 3.3 In-memory hygiene

- Decrypted secrets stored in `byte[]` or `SecureString`, never plain `string` when avoidable.
- After use, zero the byte arrays with `CryptographicOperations.ZeroMemory()`.
- Decrypted password copies in the clipboard auto-clear after the configured timeout.
- No password, OTP, token, master password, or decrypted vault secret ever appears in logs.

### 3.4 What the backend knows

The backend stores:
- User ID (UUID)
- Email (plaintext for OTP delivery)
- Phone number (hashed; plaintext only in-transit for OTP delivery)
- OAuth provider + subject ID
- TOTP MFA secret (encrypted at rest with a server-side key)
- Security question answers (Argon2id hashed)

The backend **never** stores: master password, derived keys, vault data, vault file, decrypted entries.

---

## 4. Authentication & Authorization

### 4.1 Two-layer auth model

| Layer | Purpose | Stored where |
|---|---|---|
| **Account login** | Proves user identity (email/OAuth + MFA) | Backend |
| **Master password** | Decrypts the local vault | Never stored (zero-knowledge) |

The account login is for backend services (OAuth, OTP, breach check). The master password is for the vault. They are independent.

### 4.2 OAuth providers

Supported at signup: **Google, Microsoft, Apple, Yahoo**.

- OAuth is used at signup to prove the user owns a real account.
- OAuth tokens are **not** used to derive any vault encryption key.
- After signup, the master password is the sole gatekeeper of the vault.

### 4.3 Email validation

- Email must verify via OTP.
- Disposable email providers blocked using a maintained blocklist (e.g., `disposable-email-domains` list).
- Custom domains are allowed (legitimate users may have their own).

### 4.4 Phone number validation

- Mandatory at signup.
- Verified via SMS OTP through the backend.
- Backend uses **Twilio Lookup** to flag VoIP / virtual / temp numbers and reject them.
- Known limitation: detection is not 100% accurate; this is documented for the user.

### 4.5 MFA

Required after account login, before the vault unlock screen appears.

Supported methods (user picks one or more):
- **TOTP** via authenticator app (Google Authenticator, Authy, etc.) — preferred
- **SMS OTP** via backend (Twilio)
- **Email OTP** via backend (SendGrid or AWS SES)

### 4.6 Username rules

- Unique per account.
- Stored with case as entered.
- Compared **case-insensitively** for uniqueness and login.

### 4.7 Master password rules

- Minimum length: 12 characters.
- Strength meter (zxcvbn) shown live as user types.
- Warn but do not block weak passwords (user is informed, not gated).
- No composition rules (no "must contain a symbol" — outdated).
- Master password cannot be remembered between sessions. Required every unlock.

### 4.8 Password recovery

**There is none.** Zero-knowledge means we cannot recover the master password.

- Documented clearly at signup. User must acknowledge.
- Mitigations: rolling local backups (last 5 versions), encrypted export to user-chosen location.
- Security questions exist only to recover the **account** login (for resetting MFA, changing email), not the master password.

### 4.9 Login flow

```
Account login (OAuth or email+password)
   ↓
MFA (TOTP / SMS OTP / email OTP)
   ↓
Master password prompt
   ↓
Argon2id derives key → SQLCipher opens vault
   ↓
Vault unlocked
```

Account login can be remembered (token stored in Windows Credential Manager). Master password cannot.

### 4.10 Wrong master password backoff

| Attempt | Delay before next try |
|---|---|
| 1 | 0s |
| 2 | 1s |
| 3 | 3s |
| 4 | 10s |
| 5 | 30s |
| 6+ | 60s |

**Self-destruct option** (opt-in, off by default): after 10 consecutive failed attempts, securely delete the vault file. Disabled by default because lockouts happen.

---

## 5. Vault Data Model

### 5.1 Entry types

- **Login** — title, URL, username, password, notes, tags, folder, TOTP seed (optional)
- **Secure Note** — title, body, tags, folder
- **Payment Card** — cardholder name, number, expiry, CVV, notes, tags, folder
- **Identity** — name, address, phone, email, SSN/national ID, notes, tags, folder

### 5.2 Common fields (all entry types)

- ID (UUID)
- Entry type
- Title
- Notes
- Tags (many-to-many)
- Folder (single, optional)
- Created date (UTC)
- Modified date (UTC)
- Last used date (UTC)

### 5.3 Organization

- **Folders:** single parent, optional nesting (one level deep for v1).
- **Tags:** many-to-many, freeform.
- **Search:** full-text across title, URL, username, notes, tags.

### 5.4 Auto-association

When the user stores a login, the URL is captured. When generating a new password, the user can specify the URL inline so the entry is created in one step.

---

## 6. Features

### 6.1 Password generator

- Length options: 8 / 16 / 24 / 32 / custom (8–128)
- Character sets: uppercase, lowercase, digits, symbols, ambiguous characters (toggleable)
- Generation modes: random characters, passphrase (diceware-style word list)
- One-click copy + auto-clear

### 6.2 Breach detection

- HIBP k-anonymity API (only first 5 chars of SHA-1 hash sent).
- Proxied through Lumos backend so HIBP doesn't see user IPs directly.
- Run on demand per entry, and as a batch "Vault Health Check."

### 6.3 Password strength

- zxcvbn-cs scores every stored password.
- Vault Health Check surfaces: weak, reused, breached, old (>1 year unmodified).

### 6.4 TOTP storage

- Each Login entry can store a TOTP seed.
- Lumos displays the current 6-digit code and countdown ring.
- Code can be copied to clipboard (same auto-clear rules apply).

### 6.5 QR transfer to phone

- Each entry has a "Send to phone" action.
- Generates a unique, single-use QR code containing the credentials, encrypted with a one-time key embedded in the QR itself.
- QR expires after 2 minutes.
- Each QR is unique and never regenerated.
- On first use, Lumos shows a coaching screen explaining the user must enable clipboard auto-clear on their phone before scanning. Coaching can be re-shown from Settings.

### 6.6 Auto-lock

User-configurable triggers:
- Idle timeout: 1 / 2 / 5 / 10 / 15 min / Never
- Lock on minimize: on/off (threshold 30s)
- Lock on system sleep: on/off
- Lock on screen lock: on/off

### 6.7 Clipboard auto-clear

- Timeout: 10 / 30 / 60 seconds (default 30s)
- Applies to passwords, TOTP codes, secure notes, card numbers, CVVs.

### 6.8 Audit log

Local, in-vault, append-only. Records:
- Vault opened / closed / locked
- Entry created / edited / deleted / viewed
- Failed master password attempts
- Settings changed
- Export / import performed
- Backup created

No secret values are logged — only event types and entry IDs.

### 6.9 Backups

- Rolling backup: last 5 versions of the vault file, stored in `%APPDATA%\Lumos\backups\`.
- Automatic on every successful close of a modified vault.
- Manual encrypted export to user-chosen location.

### 6.10 Import / Export

- **Import:** Bitwarden JSON (v1), generic CSV with column-mapping UI (v1). LastPass / KeePass: future.
- **Export:** Lumos encrypted format, plus optional unencrypted CSV (with strong warning).

### 6.11 Entry editing

Every entry can be edited freely after creation. No "frozen" entries.

---

## 7. UI / UX

### 7.1 Theme

**Modern cybersecurity × restrained Harry Potter.** Dark only for v1.

#### Color palette

| Token | Hex | Use |
|---|---|---|
| `--bg-deep` | `#05070F` | App background |
| `--bg-base` | `#0B0E1A` | Primary surface |
| `--surface` | `#141828` | Cards, panels |
| `--border` | `#1F2438` | Dividers |
| `--accent-gold` | `#E4B63A` | Primary CTA, brand |
| `--accent-violet` | `#8B6FE8` | Secondary accent |
| `--success` | `#4ADE80` | Unlocked, healthy |
| `--danger` | `#EF4444` | Breached, error |
| `--text-primary` | `#E8E6F0` | Body text |
| `--text-secondary` | `#9BA3B5` | Subtext |

#### Typography

- Headings: `Cinzel` or `Cormorant Garamond`
- Body / UI: `Inter` or `Geist`
- Monospace (passwords, codes): `JetBrains Mono` or `Geist Mono`

#### Motion

- Unlock: half-second golden ripple
- Lock: soft fade-to-dark
- Primary CTA: gentle pulse glow (toggleable)
- All animations respect Windows "reduce motion" setting

### 7.2 Microcopy (themed mode)

| Action | Microcopy |
|---|---|
| Unlock subtitle | "Speak the incantation" |
| Empty vault | "Your grimoire is empty. Add your first entry to begin." |
| Password generated | "A new password has been conjured" |
| Vault locked | "Vault sealed. Nox." |
| Breach detected | "This password has been seen in the wild" |

**Serious Mode toggle in Settings** strips all themed microcopy and reverts to plain language. Colors and typography stay the same.

### 7.3 Window chrome

Custom frameless window with Lumos-themed minimize / maximize / close buttons.

### 7.4 Layout

- **Left sidebar:** Vault, Generator, Audit Log, Settings
- **Center pane:** entry list + search + tag/folder filters
- **Right pane:** selected entry detail with reveal / copy / edit
- **Top bar:** prominent gold Lock button, search, user menu
- Fully responsive resizing down to a sensible minimum (720×480).

### 7.5 Keyboard shortcuts (custom / spell-named)

| Shortcut | Action | Spell name |
|---|---|---|
| `Ctrl + L` | Lock vault | Nox |
| `Ctrl + U` | Focus unlock field | Alohomora |
| `Ctrl + G` | Open generator | Geminio |
| `Ctrl + F` | Focus search | Accio |
| `Ctrl + N` | New entry | Creavit |
| `Ctrl + R` | Reveal password (selected entry) | Lumos |
| `Ctrl + C` | Copy password | (standard) |
| `Ctrl + Shift + C` | Copy username | (standard) |
| `Esc` | Close detail / cancel | (standard) |

### 7.6 Onboarding (first-run wizard)

Five screens:
1. **Welcome** — what Lumos is, zero-knowledge promise
2. **Create master password** — with strength meter and the "no recovery" acknowledgment
3. **Enable MFA** — TOTP setup (recommended), or skip for now
4. **Import existing passwords** — Bitwarden / CSV / skip
5. **Done** — into the vault

---

## 8. Logging

- **Library:** Serilog (structured logging only — no string concatenation).
- **Desktop sinks:** rolling file at `%APPDATA%\Lumos\logs\`, daily rotation, 30-day retention.
- **Backend sinks:** stdout for now (host-dependent later).
- **Levels:** `Debug` only in dev builds. `Information` and up in release.
- **Safe-log wrapper:** a `SafeLog` static class exposes only typed event methods (`VaultUnlocked(userId)`, `EntryCreated(entryId)`, etc.) — no method accepts a raw secret-typed parameter. Code review enforces use of `SafeLog` over direct Serilog calls.
- **PII masking:** phone numbers and emails masked in logs (`+91****1234`, `j****@example.com`).
- **Forbidden in logs:** passwords (any kind), OTP codes, TOTP seeds, OAuth tokens, master password, derived keys, decrypted vault content, full URLs of entries.

---

## 9. Telemetry

**None.** No analytics, no crash reporting that ships data off-device, no remote feature flags. Crash logs stay local. This is a trust commitment, not a v1-only stance.

---

## 10. Project Structure (proposed)

```
lumos/
├── src/
│   ├── Lumos.Desktop/           # WPF app
│   │   ├── App.xaml
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Services/            # VaultService, CryptoService, ClipboardService...
│   │   ├── Resources/           # Themes, fonts, icons
│   │   └── Lumos.Desktop.csproj
│   ├── Lumos.Core/              # shared models, crypto primitives
│   │   ├── Crypto/
│   │   ├── Models/
│   │   └── Lumos.Core.csproj
│   └── Lumos.Backend/           # ASP.NET Core minimal API
│       ├── Program.cs
│       ├── Endpoints/           # OAuth, OTP, HIBP proxy, Lookup
│       ├── Services/
│       └── Lumos.Backend.csproj
├── tests/
│   ├── Lumos.Core.Tests/
│   ├── Lumos.Desktop.Tests/
│   └── Lumos.Backend.Tests/
├── docs/
│   └── Lumos-Spec.md            # this document
└── Lumos.sln
```

---

## 11. Build Order (suggested)

1. **Crypto core** — Argon2id KDF, SQLCipher integration, key derivation tests.
2. **Vault file lifecycle** — create vault, open vault, close vault, basic schema.
3. **Master password flow** — create, change, verify with backoff curve.
4. **Entry CRUD** — Login entry first, then Secure Note, Card, Identity.
5. **Password generator** — random + passphrase modes.
6. **Auto-lock + clipboard auto-clear** — wire to OS events.
7. **UI shell** — sidebar, theme, fonts, dark window chrome.
8. **Backend skeleton** — minimal API project, SQLite, health endpoint.
9. **OAuth signup flow** — Google first, then Microsoft / Apple / Yahoo.
10. **OTP delivery** — SMS via Twilio, email via SendGrid/SES.
11. **MFA** — TOTP setup + verify, SMS/email OTP verify.
12. **HIBP proxy + breach detection UI.**
13. **TOTP storage for entries.**
14. **Tags, folders, search.**
15. **Audit log.**
16. **Import (Bitwarden JSON, CSV) + Export.**
17. **Rolling backups.**
18. **QR transfer to phone.**
19. **Onboarding wizard.**
20. **Polish, animations, microcopy, Serious Mode toggle.**

---

## 12. Out of Scope (v1)

- Browser extension
- Cloud sync
- Mobile companion app (PWA)
- Sharing entries between users
- Light theme
- LastPass / KeePass import
- Telemetry of any kind
- Password recovery / master password reset

---

## 13. Open Items

- Backend production hosting (decide after test phase)
- Whether to add a light theme later
- Whether to add a mobile companion (PWA) later
- Custom font licensing (Cinzel and Inter are both OFL/free; confirm at integration time)

---

*Lumos maxima.*
