# Lumos — Architecture

How Lumos is built. Reflects the **v2 (offline-only)** target as of Phase 10.

## Projects
```
Lumos.sln
├── src/
│   ├── Lumos.Core/        # All logic: crypto, vault, entries, generator, TOTP, export
│   └── Lumos.Desktop/     # WPF UI (net8.0-windows). MVVM. No business logic of its own.
└── tests/
    └── Lumos.Core.Tests/  # xUnit. Everything testable lives in Core.
```
After Phase 10 there is **no `Lumos.Backend` project** and **no network code anywhere**.

### Why the split
`Lumos.Core` has zero WPF dependencies — a plain class library, fully unit-testable.
Desktop is a thin MVVM shell binding to Core services.

## Core layout (`Lumos.Core`)
```
Crypto/     Argon2Kdf, AesGcmCrypto, KdfParameters (m=64MB,t=3,p=1,32B), SecureMemory,
            MasterPasswordPolicy, PasswordStrengthService
Vault/      VaultService (opens SQLCipher DB), VaultManager, VaultHeader, SchemaMigrator,
            FailedAttemptTracker, UnlockBackoff, UnlockResult
Entries/    Entry, EntryPayload (Login/Card/Identity/SecureNote), EntryType,
            EntryRepository (CRUD + FTS5), Folder(Repository), TagRepository, PayloadJson
Generator/  PasswordGenerator, RandomPasswordOptions, PassphraseOptions, EffWordList
Totp/       Base32 (RFC 4648), TotpGenerator (RFC 6238; offline)
PortableExport/  LumosExport, BitwardenExport, ExportEnvelope (LXP1), VaultExporter,
            VaultImporter
Security/   AutoLockService, AutoLockSettings, ClipboardService, IClipboard,
            IIdleMonitor, ISystemEventSource
```
(Phase 12 adds `Attachments/` for encrypted file blobs.)

## Desktop layout (`Lumos.Desktop`)
```
App.xaml(.cs)      startup, global exception sinks, crash logging
MainWindow.xaml.cs frameless window; routes Unlock <-> Shell
Common/            ObservableObject, RelayCommand, AppPaths, AppServices
Platform/          WindowsClipboard, WindowsIdleMonitor, WindowsSystemEventSource,
                   IFileDialogService + WindowsFileDialogService
ViewModels/        one VM per view; logic delegated to Core
Views/             XAML user controls
Resources/Themes/  Colors.xaml, Controls.xaml, FormControls.xaml
```
Shell panes (v2): **VAULT / GENERATOR / BACKUP**. (ACCOUNT removed with the backend.)

## Data on disk
```
%APPDATA%\Lumos\
├── vault.db              SQLCipher-encrypted SQLite (the vault) — ciphertext at rest
├── vault.db.header.json  KDF params + wrapped vault key; must travel with vault.db
├── crash.log             global exception log; safe to delete
└── (license file, added Phase 15)
```
No backend dir, no session file, no logs containing user data.

## Key lifecycle (the security spine)
1. User types master password.
2. Argon2id derives a 32-byte key from (password, salt-from-header). Slow on purpose.
3. That key unwraps the vault key from the header envelope.
4. The vault key opens the SQLCipher connection. Decrypted data exists only in RAM.
5. On lock/idle/close: connection closed, keys zeroed, RAM plaintext dropped.

The master password is never persisted. The vault key exists only while unlocked.

## Error handling (App.xaml.cs)
Three sinks so nothing crashes silently: `DispatcherUnhandledException` (UI thread, shows
one message box, keeps alive), `AppDomain.UnhandledException` (last resort, logs),
`TaskScheduler.UnobservedTaskException` (logs, marks observed). All write full stack
traces to crash.log. Repeat messages within 5s are de-duplicated. Wrapper exceptions
(TargetInvocation/Aggregate/XamlParse) are unwrapped to the real cause.
