# Credits & Project Notes

This file tells the story behind Lumos — what it is, the thinking that shaped it,
how it was built and tested, and who worked on it.

---

## What Lumos is

Lumos is a free, local-first, offline password manager for Windows. The goal was
simple to state and hard to do well: keep a person's passwords encrypted on their
own machine, never send anything over the network, and be honest about exactly
what that does and doesn't protect.

It started life as a more ambitious app with an online backend (accounts, sign-in,
server-side breach detection, account MFA). Partway through, the direction
changed deliberately: strip all of that out and become a **purely offline** tool.
The reasoning drove most of what followed.

---

## Thought process & key decisions

A few principles guided the design. Each one came with a tradeoff that was chosen
on purpose, not by accident.

**The smallest attack surface is the one that doesn't exist.**
Rather than securing a backend, the backend was removed entirely. An app that
makes no network calls cannot leak data over the network — the capability simply
isn't there. The only exception is the update check, and that only runs when the
user clicks a button. This single decision removed accounts, sign-in, sessions,
and an entire class of risk.

**The master password is everything, and there is no recovery.**
The vault is encrypted with a key derived from the master password using Argon2id
(a deliberately slow, memory-hard function), and that wraps a random key which
SQLCipher uses for AES-256 encryption. The password is never stored. This means a
forgotten password is unrecoverable — an intentional choice, because a recovery
backdoor would also be an attacker's backdoor.

**Honesty over reassurance.**
Lots of software overstates its security. Lumos tries to do the opposite. The
threat model spells out what it does *not* protect against — a known master
password, malware running on the same machine, a determined reverse-engineer
defeating the (cosmetic) product key. These limits are inherent to any local
password manager, so they're documented rather than hidden.

**The product key is "feels official," not protection.**
Because the app is free, the activation key exists to make distribution feel
deliberate, not to stop copying. The validation logic ships inside the app and
.NET code can be decompiled, so a determined person could forge keys. That's
fine, and it's stated plainly rather than dressed up as security.

**Calm, sharp, serious design.**
The interface went through a full visual pass toward a black-dominant, sharp-edged,
low-glow look — steel-blue as the working accent, gold reserved only for the brand
mark and the two-factor countdown ring. The aim was a tool that feels focused and
trustworthy rather than flashy.

---

## How it's built

Lumos is a .NET 8 application split into clean layers:

- **Lumos.Core** — all the logic: cryptography, the vault, entry types, the
  password generator, time-based one-time passwords, attachments, licensing, and
  import/export. It has no UI dependencies, which keeps it testable and honest
  about where the real work happens.
- **Lumos.Desktop** — the Windows interface (WPF), built in an MVVM style so the
  UI stays a thin layer over the Core logic.
- **tools/keygen** — a small private utility for generating product keys.

It's packaged with Velopack into a single self-contained installer, so users need
nothing else installed — no runtime, no dependencies. Updates are delivered
through GitHub Releases and applied from inside the app, only when the user asks.

---

## How it was tested

Testing focused on the layer that matters most: the security-critical Core.

- The Core logic is covered by an automated test suite (xUnit) spanning the
  cryptography, key derivation, the vault lifecycle, entry storage and search,
  the password generator, attachments, import/export round-trips, auto-lock
  behavior, and product-key validation. Every release was checked against the
  full suite passing with zero failures.
- The build was developed iteratively in phases, each one verified before moving
  on: build clean, tests green, then hands-on testing of the actual feature in
  the running app (unlocking, attaching files, generating keys, checking for
  updates, and so on).
- Real bugs surfaced and were fixed along the way — including a subtle one where
  a handful of dictionary words contained hyphens that collided with the
  passphrase separator, and another where attachment cleanup couldn't rely on a
  database setting that connection pooling could reset. Both were fixed at the
  root rather than patched around.
- Edge cases were tested directly: oversized attachments, tampered keys, garbage
  input, wrong passwords, and the behavior of the app when run in development
  versus installed.

The guiding rule throughout: if a change touched anything security-related, it
didn't ship until the tests proved it and the behavior was confirmed by hand.

---

## A note on the journey

This project was built collaboratively and incrementally, with a lot of back and
forth — design check-ins before each phase, screenshots to confirm the look,
build logs to confirm the behavior, and honest course-corrections when something
wasn't right. It wasn't written in one pass; it was shaped through testing,
feedback, and a fair number of "actually, let's fix that properly" moments.

---

## Credits

**Alana** — _Product Engineer, development & design__

**Arrowh** — _Product Engineer, development & testing_

---

*Lumos is free software, provided as-is with no warranty. If it keeps your
passwords safe and out of the cloud, it did its job.*
