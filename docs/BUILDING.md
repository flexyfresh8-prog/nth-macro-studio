# Building NTH Macro Studio

## What you need

**To run the app**

| Requirement | Notes |
| --- | --- |
| Windows 10 (1809+) or Windows 11 | The macro engine uses Windows input hooks |
| WebView2 Runtime | Preinstalled on Windows 11; download from <https://developer.microsoft.com/microsoft-edge/webview2/> |
| .NET 8 Desktop Runtime | Only for the framework-dependent build. The `-SelfContained` build needs nothing |

**To build**

| Requirement | Notes |
| --- | --- |
| .NET SDK 8 or newer | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| Any OS | Windows, Linux and macOS all work — the project sets `EnableWindowsTargeting=true`, so even Linux/macOS can cross-compile the Windows executable |

## One-command build (Windows)

```powershell
git clone https://github.com/<you>/nth-macro-studio.git
cd nth-macro-studio
.\build.ps1                 # framework-dependent -> dist\
.\build.ps1 -SelfContained  # standalone         -> dist\
.\build.cmd                 # same thing from plain cmd.exe
```

Output: `dist\NTH Macro Studio.exe`

## Raw dotnet commands

```bash
# framework-dependent (small, ~5 MB + runtimes)
dotnet publish src/NthMacroStudio/NthMacroStudio.csproj -c Release -r win-x64 --self-contained false -o dist

# self-contained single-folder (no runtime prerequisites on target PCs)
dotnet publish src/NthMacroStudio/NthMacroStudio.csproj -c Release -r win-x64 --self-contained true -o dist

# cross-compile from Linux/macOS — exactly the same command works
```

## Build matrix

| Variant | Size | Needs on target PC | Use when |
| --- | --- | --- | --- |
| Framework-dependent | ~5–15 MB | .NET 8 Desktop Runtime + WebView2 Runtime | You maintain the machines |
| Self-contained | ~150 MB | WebView2 Runtime only (bundled .NET) | Distributing to other people |

## Continuous integration

`.github/workflows/build.yml` builds **both** variants on every push to `main` and uploads them as workflow artifacts. Pushing a tag `v1.2.3` additionally publishes a GitHub Release containing both zips automatically.

## Troubleshooting

**NuGet restore fails** — the WebView2 package comes from nuget.org; check proxy/firewall access, or pre-restore on a connected machine and copy the `obj/` folder.

**`Microsoft.WindowsDesktop.App.Ref` not found on Linux/macOS** — the SDK downloads Windows targeting packs automatically thanks to `EnableWindowsTargeting=true`; make sure the machine can reach `nuget.org` and `aka.ms`.

**`WebView2Loader.dll` not found at runtime** — do not delete `obj/`/`bin/` manually after publishing; republish instead. The loader ships inside the WebView2 NuGet package and is copied to the output automatically.

**SmartScreen warning on first run** — the binaries are unsigned (see `docs/RELEASING.md` for signing and for the "More info → Run anyway" path).

**App opens then closes immediately** — the WebView2 Runtime is missing; install it from the link above and relaunch.
