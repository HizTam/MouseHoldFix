# MouseHoldFix

<img src="assets/MouseHoldFix.png" alt="MouseHoldFix icon" width="80">

**Keep a drag going when the left mouse button briefly releases by itself.**

Latest release: **1.0.0** (2026-10-04).

Windows tray utility · **HizTam** · [MIT license](LICENSE.txt)

[日本語の操作説明](README.ja.md)

## Examples of symptoms MouseHoldFix can improve

- While selecting a screen capture region with `Win + Shift + S`, the selection finishes before you release the button.
- Text selection in a web browser or editor ends partway through a drag.
- Immediately afterward, dragging starts moving text that was already selected.
- A file drag and drop ends before you release the button.
- Ordinary click testing sites may show no chattering, even though dragging is interrupted.

These are common examples. MouseHoldFix can also help with other symptoms caused
by a brief, unintended left-button release followed by another press while you
are still holding the button. It waits briefly before accepting the release,
allowing the drag to continue when the button is reported as pressed again.

## Start

Requires **Windows 10 / 11, x64**. Run the preparation file below as administrator. MouseHoldFix itself normally runs without administrator rights.

Choose either package:

| ZIP filename ending | Runtime preparation |
| --- | --- |
| `win-x64-self-contained.zip` | .NET is included. Proceed to extraction and startup below. |
| `win-x64-framework-dependent.zip` | Install the **.NET 10 Desktop Runtime for Windows x64** first. |

### Install the runtime for the framework-dependent package

1. Open the [official Microsoft .NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Find **.NET Desktop Runtime**, then choose **Windows → Installers → x64** for the latest **10.0.x** release.
3. Download and run the installer, and complete the installation. If this runtime is already installed, proceed to startup below.

The Desktop Runtime includes the general .NET Runtime. Running MouseHoldFix only requires the Desktop Runtime.

### Important: prepare before running the EXE

**Complete these steps before running `MouseHoldFix.exe` for the first time.**

This preparation addresses first-start blocking in Windows, including environments with **Smart App Control** on Windows 11. Removing download-origin information before the first launch allowed MouseHoldFix to start in a reported Windows environment. The result can depend on Windows security settings; unblocking may not resolve every Smart App Control block. [Microsoft's explanation](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions).

1. Right-click the ZIP in File Explorer, choose **Extract All…**, and extract the entire archive to a folder. Keep all extracted files and folders together.
2. In the extracted folder, right-click **`Prepare-MouseHoldFix.cmd`** and choose **Run as administrator**. Confirm the Windows prompt.
3. Check that the window reports **Preparation completed**, then press a key to close it. If an error is shown, resolve it before starting the EXE.

Once preparation is complete, continue with **Start MouseHoldFix** below. Repeat these steps **before running any newly downloaded copy for the first time**.

If Windows blocks the preparation file itself, open the downloaded ZIP's **Properties**, select **Unblock** if available, apply the change, and extract the ZIP again before following the steps above.

### Start MouseHoldFix

1. Open the extracted folder and run `MouseHoldFix.exe` normally.
2. Find the mouse icon in the system tray, including the hidden icons area.
3. Double-click the icon to open **Settings**. Keep **Enable repair** and **Repair only while dragging** enabled.
4. Start with the default **15 ms**, press **Save**, and try the operation that was interrupted.

The UI uses Japanese on Japanese Windows systems and English on other systems.
All screenshots below show the **English UI**.

## Settings

<img src="assets/screenshots/settings.en.png" alt="MouseHoldFix settings in English" width="560">

| Setting | How to use it |
| --- | --- |
| Enable repair | Turn correction on or off. Default: on. |
| Release grace period | How long to wait before accepting a release. Range: **1–200 ms**; default: **15 ms**. |
| Repair only while dragging | Recommended for normal use. Keeps ordinary clicks and double-clicks responsive. Default: on. |
| Enable diagnostic logging | Record mouse events locally when investigating a problem. Default: off. |
| Start when signing in to Windows | Start the app automatically when you sign in. Default: off. |

Press **Save** to apply changes. **Cancel** or **Esc** discards unsaved edits.

If a drag still ends unexpectedly, gradually increase the grace period. Use the
smallest value that works: a longer period also delays the release of a real
drag. Turning drag-only correction off also delays ordinary clicks and may
combine rapid clicks.

## Tray menu

<img src="assets/screenshots/tray-menu.en.png" alt="MouseHoldFix tray menu in English" width="300">

- **Enable repair**: switch correction on or off immediately.
- **Grace period**: choose **5 / 10 / 15 / 20 / 30 / 50 / 75 / 100 / 125 / 150 / 200 ms**. The selected value is applied and saved immediately.
- **Settings…**: configure the options above.
- **Diagnostics…**: view the current correction status, counters, and recent events.
- **Open log folder**: open the local log files.
- **Start with Windows**: switch automatic startup on or off.
- **Help**: display the operating instructions.
- **About**: display version information.
- **Exit**: stop correction and close the app.

A green check on the tray icon indicates enabled correction; a gray minus
indicates disabled correction.

## Help

Help uses Japanese on Japanese Windows systems and English elsewhere. The
language link at the top opens the other language inside the same window.

## Investigate an interrupted drag

<img src="assets/screenshots/diagnostics.en.png" alt="MouseHoldFix diagnostics in English" width="840">

1. Turn **Enable repair** off and **Enable diagnostic logging** on, then save.
2. Repeat the operation that causes the interruption.
3. Open **Diagnostics…** and check **Last Up → Down** and the log.
4. Choose a grace period long enough to cover the unintended gap, enable repair with drag-only correction on, and try again.
5. Turn diagnostic logging off after investigating.

The **Enable repair and logging** button enables both using the current grace
period. Repair must be enabled for an interruption to be corrected.

Choose **Delete logs** in Diagnostics to move the entire log folder to the
Recycle Bin. This button is disabled while recording; turn **Enable diagnostic
logging** off before using it. When the log folder does not exist, both **Delete
logs** and **Open log folder** are disabled. The tray's **Open log folder**
item is also disabled in that case.

## Supported input

Correction applies to left-button input from all mice and touchpads reported
as mice. Right and middle buttons and touch gestures are outside its scope.
It cannot correct a fault that keeps reporting the left button as released
while you are holding it down. Correction may be restricted when the target
app runs as administrator.

## Update or remove

Choose **Exit** from the tray before replacing the app with a new version.
Prepare the newly downloaded files using the first-start unblocking steps above before launching the replacement.
If automatic startup is enabled, disable it before moving or removing the
app, and enable it again from the new location if needed.

To remove the app, exit and delete its folder. To also remove saved settings
and logs, delete `%LOCALAPPDATA%\MouseHoldFix`.

## Security and privacy

MouseHoldFix itself **does not perform any network communication**. It does not
check for updates, send usage data, or upload settings or logs. It does not
record keyboard input.

Settings and logs stay on this PC under `%LOCALAPPDATA%\MouseHoldFix`.
Diagnostic logs include mouse positions and timestamps. Use **Delete logs**
in Diagnostics to move unwanted logs to the Recycle Bin.

Clicking an external link in Help opens the web page in your default browser.
Any network communication for that page is performed by the browser.

## What's New in 1.0.0

Initial release.

- Stable release for Windows 10 / 11 x64, repairing brief left-button releases that interrupt screen captures, text selection, and file dragging.

## Changelog

See [CHANGELOG](CHANGELOG.md) for the full release history.

License details: [LICENSE.txt](LICENSE.txt) · [Third-party notices](THIRD-PARTY-NOTICES.md)

[![GitHub Sponsors](https://img.shields.io/badge/GitHub%20Sponsors-Support%20this%20project-ea4aaa?logo=githubsponsors)](https://github.com/sponsors/hiztam)
