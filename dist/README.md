# dist

## `android-latest.json`

The update feed for the Android app. Lumos fetches it from
`raw.githubusercontent.com/Arrowh-0h/LUMOS/main/dist/android-latest.json`
when the user taps **Check for updates**, and from nowhere else.

**Committed here rather than attached to a release on purpose.** GitHub's
`/releases/latest/download/<asset>` resolves to the newest release of *any*
kind, so publishing a Windows release would change what the Android app sees.
A file at a fixed path in the default branch is unambiguous. The cost is one
commit per Android release.

### Fields

| Field | Meaning |
|---|---|
| `versionCode` | Integer. Must match `ApplicationVersion` in the APK exactly. The app compares this number, never the name. |
| `versionName` | Shown to the user, e.g. `2.0.1`. |
| `url` | HTTPS URL of the signed APK. The app refuses anything that is not https. |
| `sha256` | Lowercase hex hash of that exact APK file. |
| `notes` | One short line. Optional. |

### Publishing a release

```powershell
# 1. bump BOTH numbers in Lumos.Android.csproj (the build fails if they disagree)
# 2. build
dotnet publish src\Lumos.Android\Lumos.Android.csproj -c Release -f net8.0-android

# 3. confirm it is signed with the right key
& "C:\Android\Sdk\build-tools\35.0.0\apksigner.bat" verify --print-certs <apk>
#    SHA-256 must be e3e072810343f09b990f7b0e0ae3ba7d74158be59324ed9a008241b03e2967a0

# 4. hash the APK
(Get-FileHash <apk> -Algorithm SHA256).Hash.ToLower()

# 5. publish a GitHub release tagged android-v<version> with the APK attached
# 6. update this file and commit it
```

**Step 6 is what makes the update visible.** Steps 5 and 6 must agree — a URL
and hash that do not match the published file will fail the integrity check on
every user's phone. That fails safely (the download is discarded) but is
confusing to diagnose.

**Never lower `versionCode`, and never reuse one.** Android compares it
numerically and silently declines an install that is not strictly newer, which
users experience as the update button doing nothing.

### Rolling back a bad release

You cannot. Not by lowering the number, anyway. Publish a *higher* versionCode
containing the older code — e.g. ship 2.0.2 with 2.0.0's behaviour. Pointing
this file back at an older APK will not work: any user who already took the bad
update has a higher versionCode installed and Android will refuse the downgrade.
