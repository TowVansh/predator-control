# Predator Control (PH16-71 fork)

A small tray app that runs an Acer Predator Helios 16 (PH16-71, i9-13900HX / RTX 4070) without PredatorSense.
Forked from [supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper) and rewritten from scratch.

## What it does

- **Smart mode (default).** The app decides the power mode, CPU boost and fans by itself:
  - Idle: Quiet mode with boost off. Idle temperature goes from about 78°C to about 58°C, and the fan from about 5300 to 1800 rpm.
  - Light use: Balanced mode with boost.
  - Heavy load or gaming: Turbo with boost. Games are detected from GPU load, fullscreen windows, or your games list.
  - Thermal limit: keeps the CPU at 90°C or below. It raises the fans first, then turns CPU boost off, and caps clocks only as a last resort.
  - Battery: never Turbo. Below 20% battery it forces Quiet.
  - Every decision is logged to `%LOCALAPPDATA%\PredatorControl\smart.log`.
- **Manual modes:** Quiet, Balanced, Performance and Turbo, with Auto, Max or a custom fan curve.
- **Per-key keyboard RGB** over USB, with no Acer software. Effects are Off, Static, Breathing, Wave and Rainbow. Smart lighting turns the keyboard off after 3 idle minutes and dims it on battery.
- **Games list:** executables that are always treated as games.
- Tray menu, and start with Windows through a scheduled task, so there's no UAC prompt at logon.

Settings live in `%LOCALAPPDATA%\PredatorControl\settings.json`.

## Hardware notes (measured on PH16-71)

| Thing | Finding |
|---|---|
| Acer WMI (`root\WMI\AcerGamingFunction`) | Power modes 0/1/4/5, fan Auto/Max/Custom and the sensors all work. Keyboard lighting over WMI does nothing on this model. |
| Keyboard | Per-key RGB on USB HID `04F2:0117`, interface 3, usage page `FF02`. The protocol is documented in `Predator/Hardware/AcerUsbKeyboard.cs`. |
| CPU thermals | With boost on, the CPU holds 91°C, the firmware's TCC limit, in every Acer mode. Boost off gives about 75°C at about 25% lower multi-core clocks. Max fans give no extra clocks. |
| CPU fan custom speed | Ignored by the EC while the CPU is hot. The GPU fan follows it. |

## Build

```
dotnet build Predator/Predator.csproj -c Release
dotnet test Predator.Tests
dotnet publish Predator/Predator.csproj -c Release -o app
```

The app needs admin rights, set in its manifest, and the .NET 8 Desktop Runtime.

`tools/` holds the reverse-engineering and test scripts: `rgb_*.py`, `re_dll.py`, `stress.py` and `snap.ps1`. It also holds `optimize.ps1`, the Windows tune-up script, which supports `-Revert`.

## License

MIT, as upstream. Use at your own risk.
