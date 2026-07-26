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
> Because Lumos is fully open source, you can read every line of the code here and
> **build the installer yourself** from source if you'd rather not trust the
> prebuilt binary (see the developer section below).

**Requirements:** Windows 10 or 11. The installer above is **x64**, which also
runs on ARM64 machines through Windows' built-in emulation.

### On ARM64?

A native ARM64 build is available, but **only from
[the Lumos website](https://lumos-app.netlify.app/)** — not from GitHub Releases.

The reason: both architectures would otherwise share a single Velopack update
feed, and an ARM64 install polling it would be offered the x64 package and
replace itself with the wrong architecture. Rather than risk that, the ARM64
build is distributed separately.

> **ARM64 users:** the in-app **Check for updates** is disabled on your build —
> it will tell you to visit the website instead. To update, download the newest
> ARM64 installer from the site and run it over your existing install. Your vault
> is never touched by an update.

This is a temporary arrangement while proper per-architecture update channels are
set up.

---

## 🔑 If you forget your master password

When you create a vault, Lumos gives you a **recovery code** — 30 characters, in
six groups of five. It is the only other way into your vault.

```
ABCDE-FGHJK-MNPQR-STVWX-YZ234-56789
```

Save it somewhere separate from your computer: printed and filed, or in a safe.
Storing it next to the vault defeats the point.

A few things worth being blunt about:

- **The code is shown once.** Lumos stores only an encrypted envelope that your
  code can open, never the code itself. If you lose it, you can generate a new
  one — but only while you still know your master password.
- **Nobody can issue one for you.** Not me, not anyone. There is no server, no
  account, and no master key. Any system where the developer could restore your
  access would be a backdoor into every user's vault.
- **If you lose both your master password and your recovery code, your data is
  gone.** Permanently. That is the honest cost of encryption that actually works.

Already using Lumos from before recovery codes existed? You'll be offered one the
first time you unlock after updating. Your vault and password don't change.

---

## Features

- **Strong encryption** — your vault is protected with Argon2id + AES-256
  (via SQLCipher). Without your master password, the vault file is unreadable.
- **Fully offline** — Lumos makes no network connections at all, except when
  *you* click "Check for updates." Nothing is ever sent anywhere.
- **Four entry types** — logins, secure notes, cards, and identities.
- **File attachments** — attach files to any entry (up to 50 MB each), stored
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

- The installer isn't code-signed, which is why Windows warns about it.
- There is no account, no telemetry, and no server. Lumos never makes a network
  request except when you explicitly check for an update.
- Your recovery code is generated by the app and never leaves your machine. I
  cannot recover your vault for you, and I've deliberately built it so that I
  couldn't even if asked.
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
docs/                Architecture, decisions, security, recovery & build notes
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
