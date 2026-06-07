# EFF Wordlist Setup

The password generator's passphrase mode uses the EFF Large Wordlist
(7,776 deliberately memorable, distinct, non-offensive English words).
This file isn't bundled with the source code — you need to download it
once and place it at the right path before building.

## Setup (one time, ~2 minutes)

1. **Create the Resources folder** if it doesn't exist:
   ```powershell
   mkdir src\Lumos.Core\Resources
   ```

2. **Download the EFF Large Wordlist** to that folder:
   ```powershell
   Invoke-WebRequest -Uri "https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt" -OutFile "src\Lumos.Core\Resources\eff_large_wordlist.txt"
   ```

3. **Verify the download** — the file should be about 100 KB and contain 7,776 lines:
   ```powershell
   (Get-Content src\Lumos.Core\Resources\eff_large_wordlist.txt).Count
   # should print: 7776
   ```

4. **Build and test:**
   ```powershell
   dotnet build
   dotnet test tests/Lumos.Core.Tests
   ```

## What if I skip the download?

The build will still succeed (the `<EmbeddedResource>` entry in the
`.csproj` uses a `Condition="Exists(...)"` clause that quietly skips when
the file is missing). However:

- `PasswordGenerator.GeneratePassphrase(...)` will throw with a clear
  error message at first call.
- Tests that depend on the wordlist will pass-by-skip (they check
  availability first and return early).

So you can build and run the rest of Lumos without it, but passphrase
generation won't work until you add the file.

## Why the EFF Large Wordlist?

- **7,776 words** — exactly enough for 5 dice rolls per word (6^5).
  Five-word passphrases hit 77 bits of entropy, six-word hits 93 bits.
- **Memorable** — words chosen to be common, distinct, and easy to type.
- **Non-offensive** — the EFF filtered out anything potentially
  inappropriate.
- **Standard** — this is the recognized list for diceware. Other tools
  (1Password's word generator, Bitwarden's, etc.) produce comparable
  output against this list.

## Source

https://www.eff.org/dice
