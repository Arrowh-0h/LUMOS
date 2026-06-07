# Lumos — Licensing & Product Keys

How Lumos's product-key activation works, and how you issue keys.

## What it is (and isn't)

Lumos is **free**. The product key is a "feels official" activation gate, not a
copy-protection or revenue mechanism. It blocks the app on first launch until a
valid key is entered, then never asks again on that machine/user.

**Honest limitation (Decision D-V2-07):** the validation secret ships inside the
app, and .NET compiles to IL that decompiles cleanly (ILSpy, dnSpy, etc.). A
determined person can extract the secret and generate their own valid keys. This
scheme makes that *effort* necessary — it resists casual guessing and behaves
like a real product key — but it is deliberately not unbreakable. That tradeoff
is acceptable because the product is free; we document it rather than pretend
otherwise.

## Key format

```
LUMOS-XXXX-XXXX-XXXX
```

- 12 payload characters from an unambiguous base32 alphabet
  (`ABCDEFGHJKMNPQRSTVWXYZ23456789` — no 0/O, 1/I/L, U).
- First 8 chars: a random **serial**.
- Last 4 chars: a **partial signature** = HMAC-SHA256(serial, secret) truncated
  and mapped into the alphabet.
- Validation recomputes the signature from the serial and compares. Only the
  holder of the secret can produce serial+signature pairs that validate.

Input is forgiving: case-insensitive, and dashes/spaces/the `LUMOS` prefix are
optional when entering.

## Issuing a key to someone

When a user emails you asking for a key:

```powershell
# Generate one key:
dotnet run --project tools/keygen

# Or several at once:
dotnet run --project tools/keygen -- 10
```

Copy a line from the output and send it. That's the whole workflow — no list to
maintain, no server, fully offline. The generator uses the same algorithm and
secret as the app, so every key it prints will validate (it self-checks before
printing).

## The shared secret

Both the in-app validator (`src/Lumos.Core/Licensing/ProductKey.cs`) and the
generator use the same `Secret` constant in `ProductKey.cs`.

- **Changing the secret invalidates every previously issued key** — useful if you
  ever want to start a "new batch" and retire old keys.
- If you change it, rebuild and re-release the app, and regenerate any keys you
  hand out afterward.

## Where activation is stored

`%APPDATA%\Lumos\license.dat` — the validated key, encrypted with Windows DPAPI
(per-user). Because activation is checked *before* the vault unlocks, it can't
use the master-password key; DPAPI ties it to the Windows user instead.

- Copying `license.dat` to another user account won't work (DPAPI is per-user).
- The stored key is re-validated with `ProductKey.IsValid` on every launch, so a
  hand-edited file won't activate unless it holds a genuinely valid key.
- To reset activation (e.g. for testing): delete `license.dat`, or call
  `LicenseStore.Clear()`.

## Files

- `src/Lumos.Core/Licensing/ProductKey.cs` — format + validation (shared).
- `src/Lumos.Desktop/Common/LicenseStore.cs` — DPAPI-encrypted activation state.
- `src/Lumos.Desktop/ViewModels/ActivationViewModel.cs` + `Views/ActivationView.xaml`
  — the first-launch gate UI.
- `tools/keygen/` — the private key generator (NOT shipped with the app; keep it
  in the repo but it isn't in the app build or installer).
