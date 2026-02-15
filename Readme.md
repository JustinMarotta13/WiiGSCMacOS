# WiiGSC for macOS

A macOS-native port of **WiiGSC (Wii Game Shortcut Creator)** — create Wii channel forwarders from your game library with authentic banners, icons, and working game launch.

## Features

- Create WAD forwarder channels from WBFS/ISO game files
- Extracts authentic banners and icons directly from game images using [wit (Wiimms ISO Tools)](https://wit.wiimm.de/)
- Supports USB Loader GX, WiiFlow, Yal, and Configurable forwarder DOLs
- Downloads cover art from GameTDB
- Built-in WAD validator with brick-prevention safety checks
- Native macOS app bundle (Apple Silicon / arm64)

## Requirements

- macOS (Apple Silicon)
- .NET 8.0 SDK (for building)
- [wit (Wiimms ISO Tools)](https://wit.wiimm.de/) installed at `/usr/local/bin/wit`

## Building

```bash
./build-macos.sh
```

This produces `WiiGSC.app` in the project root.

## Usage

1. Open WiiGSC.app
2. Select a WBFS or ISO game file (filename should include the disc ID, e.g. `Wii Sports [RSPE01].wbfs`)
3. Choose your USB loader type
4. Select an output path for the WAD
5. Click **Create WAD**
6. Install the resulting WAD to your Wii using a WAD manager

## Credits

- **JustinMarotta13** — macOS port using Avalonia/.NET 8
- **modmii** — original WiiGSC (Wii Game Shortcut Creator)
- **netjat76** — original Wii tools and SaveTemper source code

## License

See individual license files in the source directories.