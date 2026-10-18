<p align="center"><img src="docs/icon.png" width="72" alt="PulseDock icon"></p>

<h1 align="center">PulseDock</h1>

<p align="center">
  A small, always-visible system monitor for Windows: CPU, memory, disk, both GPUs and network, docked in a corner of your screen.<br>
  <b>English</b> · <a href="README.pt-BR.md">Português</a>
</p>

<p align="center">
  <a href="https://github.com/filipembcruz/pulsedock/releases/latest/download/PulseDock.exe"><b>⬇ Download PulseDock.exe</b></a>
  &nbsp;·&nbsp; Windows 10 (2004+) / 11 · 64-bit · free &amp; open source (MIT)
</p>

![PulseDock on the desktop](docs/screenshots/hero.jpg)

## Install (1 minute)

1. **Download** [`PulseDock.exe`](https://github.com/filipembcruz/pulsedock/releases/latest/download/PulseDock.exe). All versions are on the [Releases](https://github.com/filipembcruz/pulsedock/releases) page.
2. **Run it.** There is no installer and nothing else to install: .NET is bundled inside the `.exe`.
   - *Tip:* move the file to a permanent folder first, such as `C:\Tools\PulseDock`, because the shortcuts point to where the file is.
3. **Done.** On first launch PulseDock adds a **PulseDock** shortcut to the **Start menu** and the **desktop**. Use it to open the app again after you quit.

> **Windows says "Windows protected your PC"?** That's SmartScreen. It appears for every new app that isn't code-signed (signing certificates are paid). Click **More info → Run anyway**. The `.exe` is built by GitHub Actions directly from the source in this repository.

**Uninstall:** quit PulseDock (⋯ → *Quit*), then delete the `.exe`, the shortcuts and the `%APPDATA%\PulseDock` folder. If you turned on *Start with Windows*, turn it off before quitting.

## Screenshots

| Compact (default) | Full view | Quick menu |
|:---:|:---:|:---:|
| ![Compact layout](docs/screenshots/compact.png) | ![Full view](docs/screenshots/expanded.png) | ![Quick menu](docs/screenshots/menu.png) |

**Strip layout**

![Strip layout](docs/screenshots/strip.png)

| Top processes · end task | Settings |
|:---:|:---:|
| ![Process details](docs/screenshots/details.png) | ![Settings](docs/screenshots/settings.png) |

## Features

- **At a glance**
  - **CPU:** usage, clock and temperature¹.
  - **Memory:** used / total.
  - **Disk:** activity, read and write speed, and temperature¹.
  - **GPU 0 (integrated) and GPU 1 (dedicated):** usage, VRAM and temperature.
  - **Network:** download and upload, with a mini chart.
- **Find what's slowing you down:** click CPU, Memory or Disk to see the top 10 processes and **End task** with a confirmation. Critical Windows processes are protected.
- **Three layouts:** compact vertical (default), full view and horizontal strip.
- **Three modes:**
  - **Overlay:** always on top.
  - **Desktop only:** behind your windows.
  - **System tray:** opens from the hidden-icons area.
- **Put it anywhere:**
  - Pin it to any of the 4 corners.
  - Drag it anywhere; it snaps when dropped near a corner.
  - Lock its position.
  - Choose the monitor and the distance from the edge.
- **Extras:**
  - Click-through, hold **Ctrl** to interact.
  - Hide while a full-screen game or video is running.
  - Start with Windows.
  - Adjustable opacity and refresh rate.
- **Languages:** English (default) and Português. Change it in *Settings → Language*, or choose *Automatic* to follow Windows.

¹ CPU and disk temperatures are optional. See [Temperatures & antivirus](#temperatures--antivirus).

## How to use

| To… | Do this |
|---|---|
| Open the full view | Click **⤢** |
| Change layout, mode or corner, lock position, quit | Click **⋯**, or right-click the widget |
| Move the widget | Drag it from anywhere. Drop it near a corner to snap |
| See and end heavy processes | Click the CPU, Memory or Disk row |
| Change language, metrics, opacity, startup | ⋯ → **Settings…** |
| Lost the widget? | Tray icon → **Move widget back to its corner** |

Settings are stored in `%APPDATA%\PulseDock\settings.json`, and the log in `log.txt` in the same folder.

## Temperatures & antivirus

- **GPUs need no driver.** Usage, VRAM and temperature come from the same Windows APIs that Task Manager uses.
- **CPU and disk temperatures are opt-in and off by default.** Turn them on in *Settings → Advanced sensors*.
  - They use the **WinRing0** driver from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), and only work when PulseDock runs **as administrator**. The *Restart as admin* button is in the same section.
  - Microsoft Defender flags that driver as `HackTool:Win32/Winring0`. It isn't malware, but the driver has a known vulnerability.
  - While the option is off, the driver is never extracted or loaded.

## Build from source

You only need this if you want to change the code.

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), or run `winget install Microsoft.DotNet.SDK.8`.
2. Clone the repository and double-click **`build.bat`**. It:
   1. builds a single self-contained `PulseDock.exe`;
   2. installs it to `%LOCALAPPDATA%\Programs\PulseDock`;
   3. starts it.

Or run the build manually:

```bat
dotnet publish src\PulseDock\PulseDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

**Your own fork:** every push to `main` builds the app in GitHub Actions and publishes a new **Release** with `PulseDock.exe` attached. `publish-github.bat` creates the repository and pushes the code from your PC; change `REPO` at the top of the file first.

### Project structure

```
src/PulseDock/
  Services/    data collection (CPU, memory, disk, network; GPU via D3DKMT + performance counters;
               optional LibreHardwareMonitor), localization (Loc.cs), window placement, Win32 interop
  ViewModels/  UI state
  Views/       widget, quick menu, process details, settings, tray panel
  Themes/      colors, styles and the three widget layouts
docs/screenshots/
```

**Translations:** all UI text is in `src/PulseDock/Services/Loc.cs`, as English/Portuguese pairs. Pull requests for more languages are welcome.

Tech: C# · .NET 8 · WPF · [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (optional sensors).

## License

[MIT](LICENSE). Free to use, modify and share.
