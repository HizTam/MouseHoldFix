# Building MouseHoldFix

## Requirements

- Windows 10 / 11, x64.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build MouseHoldFix from source. Make `dotnet` available in your terminal. The SDK selection is defined in `global.json` and allows stable .NET 10 SDK feature updates.
- The scripts prefer a workspace SDK at `.tools/dotnet/dotnet.exe` when present; otherwise, they use `dotnet` on PATH.
- Run the commands below from the repository root.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1
```

The default configuration is Release. The build script restores dependencies and builds the application and its core library.

## Create release packages

To publish both distribution variants:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Publish.ps1 -Mode All
```

To publish one variant, use `-Mode SelfContained` or `-Mode FrameworkDependent`. Omitting `-Mode` publishes the self-contained variant.

- **SelfContained** includes .NET and produces a single application executable.
- **FrameworkDependent** requires the .NET 10 Desktop Runtime for Windows x64 on the machine running the app. Keep all published files together.

Both packages include the English and Japanese operating instructions, images, changelog, required license notices, and the first-start preparation files `Prepare-MouseHoldFix.cmd` and `Unblock-MouseHoldFix.ps1`. The CMD uses Windows PowerShell to unblock and run the PS1. Script paths are passed through a process environment variable, and the execution-policy option applies only to that process. The PS1 targets only the known project binaries beside itself and does not launch the application.

For version `1.0.0`, the output layout is:

```text
artifacts/releases/
  1.0.0/
    self-contained/
    framework-dependent/
  MouseHoldFix-1.0.0-win-x64-self-contained.zip
  MouseHoldFix-1.0.0-win-x64-self-contained.zip.sha256
  MouseHoldFix-1.0.0-win-x64-framework-dependent.zip
  MouseHoldFix-1.0.0-win-x64-framework-dependent.zip.sha256
```

Exit a running copy from the tray before republishing its output folder. The publisher refuses to replace files used by a running release.

## Run from the workspace

After publishing:

```powershell
.\Start-MouseHoldFix.cmd
```

To select the framework-dependent variant:

```powershell
.\Start-MouseHoldFix.cmd -FrameworkDependent
```

The launcher uses the version recorded in `artifacts/current-version.txt`, prefers the self-contained variant by default, and checks the executable's version. If MouseHoldFix is already running, it shows the running and selected versions and asks you to exit the running app from the tray before starting again.

To inspect the selected executable without starting it:

```powershell
.\Start-MouseHoldFix.cmd -CheckOnly
```

## Application version

`Directory.Build.props` is the source of the application version.
