# Releasing NTH Macro Studio

## Automated release (recommended)

1. Bump the version in `src/NthMacroStudio/NthMacroStudio.csproj` (`<Version>1.0.0</Version>`) and commit.
2. Tag and push:

   ```bash
   git tag v1.0.0
   git push origin main --tags
   ```

3. The `Build & Release` workflow builds both variants and creates a GitHub Release with:
   - `NTH-Macro-Studio-win-x64.zip` (framework-dependent)
   - `NTH-Macro-Studio-win-x64-selfcontained.zip` (standalone)
   - auto-generated release notes
4. Verify with `gh release view v1.0.0` (or the Releases page) and edit the notes if you want highlights added.

## Manual release

```powershell
.\build.ps1 -SelfContained
Compress-Archive -Path "dist/*" -DestinationPath "NTH-Macro-Studio-1.0.0.zip"
# GitHub -> Releases -> Draft a new release -> upload the zip
```

## Checksums

Publish SHA-256 hashes alongside the binaries:

```powershell
Get-FileHash .\NTH-Macro-Studio-1.0.0.zip -Algorithm SHA256
```

## Signing notes

The shipped binaries are **unsigned**, so Windows SmartScreen shows "Windows protected your PC" on first run.
Users can click **More info → Run anyway** — standard for open-source apps without a code-signing certificate.

To remove that friction:

1. Buy an Authenticode code-signing certificate (OV or EV) from a CA.
2. Store the certificate as a GitHub Actions secret (e.g. `CODESIGN_PFX` base64 + `CODESIGN_PASSWORD`).
3. Sign in the workflow before zipping:

   ```pwsh
   # decode the .pfx from the secret, import it, then:
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f cert.pfx /p $env:CODESIGN_PASSWORD "dist\NTH Macro Studio.exe"
   ```

Never commit the certificate file or its password — `.gitignore` already excludes `*.pfx` and `*.key`.
