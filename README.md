# 🔒 Lumos

**A free, local-first, offline password manager for Windows.**

Your passwords never leave your computer. No accounts, no cloud, no tracking, no
network connection. Just a strongly-encrypted vault on your own machine that only
you can open.

---

## Download

**[⬇️ Download the latest version](https://github.com/Arrowh-0h/LUMOS/releases/latest)**

Grab `Lumos-win-Setup.exe` from the latest release and run it. That's everything
you need — the installer is self-contained, so you do **not** need to install
.NET or anything else.

> **Heads up:** because Lumos is a free, independently-published app, Windows may
> show a blue *"Windows protected your PC"* SmartScreen warning the first time you
> run the installer. This is normal for apps that aren't code-signed (signing
> certificates are expensive). Click **More info → Run anyway** to continue.

**Requirements:** 64-bit Windows 10 or 11.

---

## 🔑 Activation

Lumos asks for a one-time product key the first time you open it. The app is
**completely free** — the key just keeps activations tidy.

**To get your free key, email me:**

> ✉️ **p1avan.kumar.a@gmail.com**
>

Send a quick message and I'll reply with a key in the format
`LUMOS-XXXX-XXXX-XXXX`. Enter it once and you'll never be asked again on that
computer.

---

## Features

- **Strong encryption** — your vault is protected with Argon2id + AES-256
  (via SQLCipher). Without your master password, the vault file is unreadable.
- **Fully offline** — Lumos makes no network connections at all, except when
  *you* click "Check for updates." Nothing is ever sent anywhere.
- **Four entry types** — logins, secure notes, cards, and identities.
- **File attachments** — attach files to any entry (up to 25 MB each), stored
  encrypted inside your vault. Images preview inline.
- **Built-in TOTP** — store two-factor codes with a live countdown ring.
- **Password generator** — strong random passwords or memorable passphrases.
- **Search, folders & tags** — find things fast; secrets are never indexed.
- **Auto-lock** — locks itself when you're idle, the screen locks, or the PC sleeps.
- **Clipboard auto-clear** — copied passwords are wiped from the clipboard shortly after.
- **Encrypted backup & import** — export an encrypted backup, or import from
  Bitwarden, CSV, and more.
- **Self-updating** — check for updates from inside the app whenever you choose.

---

## Your data & privacy

- Your vault lives at `%APPDATA%\Lumos\` on your own machine. **It is never
  uploaded anywhere.**
- The only thing that can open your vault is your **master password**. There is
  **no recovery** — if you forget it, the data is gone. Choose it carefully and
  don't lose it.
- Updating or uninstalling Lumos never touches your vault.

For the full technical security write-up (what Lumos protects against and what it
doesn't), see [`docs/SECURITY.md`](docs/SECURITY.md).

---

## A note on honesty

Lumos is built to be straight with you:

- The product key is a "feels official" gate, **not** copy protection — the app
  is free and the check is bypassable by anyone determined. That's fine.
- The installer isn't code-signed, which is why Windows warns about it.
- If someone learns your master password, or your computer is infected with
  malware, no password manager can save you — Lumos included. It protects a
  stolen *file*, not a compromised *computer*.

---

## For developers

Lumos is a .NET 8 / WPF app.

```
src/Lumos.Core/      Crypto, vault, entries, generator, TOTP (pure .NET 8)
src/Lumos.Desktop/   WPF app (Windows-only)
tests/               xUnit tests for the core
tools/keygen/        Product-key generator (maintainer tool)
docs/                Architecture, decisions, security, build & licensing notes
```

Build and test from the repo root on Windows:

```powershell
dotnet build
dotnet test tests/Lumos.Core.Tests
```

To produce an installer, see [`docs/BUILD-AND-RELEASE.md`](docs/BUILD-AND-RELEASE.md).

## License

Lumos is released under the [MIT License](LICENSE) — free to use, modify, and
redistribute. See the `LICENSE` file for the full text.

It builds on excellent open-source libraries, including SQLCipher / SQLite3MC
(vault encryption), Argon2 (key derivation), and Velopack (installer & updates).
Those components remain under their own respective licenses.

---

*Lumos is free software provided as-is, with no warranty.*
