# 🎮 SaveTracker Desktop

> **Version 0.5.0 pre-release** - Automatic Game Save File Tracking & Cloud Sync

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3.9-8B44AC)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows)](https://www.microsoft.com/windows)

**SaveTracker Desktop** is a powerful standalone application that automatically tracks and synchronizes your game save files to the cloud. Originally developed as a Playnite plugin, it has evolved into a feature-rich desktop app supporting all major game launchers (Steam, Epic, GOG, etc.) with advanced capabilities like headless operation and IPC integration.

⚠️ **This is beta software** - Please report any issues you encounter!

---

## ✨ Key Features

- 🔄 **Smart Tracking** - Automatically detects running games and monitors file changes in real-time
- ☁️ **Universal Cloud Sync** - Seamless integration with Google Drive, OneDrive, Dropbox, and 7+ other providers via Rclone
- 🚀 **Native Performance** - Built with .NET 8 AOT for lightning-fast startup and minimal resource usage
- 👻 **Headless Mode** - Run silently in the background with a highly optimized, ultra-lightweight executable
- 🔌 **IPC API** - Full integration support for external tools (e.g., Playnite addons) via Named Pipes
- 🎮 **Multi-Launcher Support** - Works with Steam, Epic, GOG, and any custom game executable
- 📁 **Smart File Management** - Intelligent legacy migration, blacklist editor, and manual tracking controls
- ⚡ **Optimized Transfer** - Batch upload processing for massive save folders (thousands of files)
- 🔍 **Auto-Detection** - Scans your library to find installed games automatically

---

## 📥 Installation

### Requirements
- Windows 10/11 (64-bit)
- Administrator rights (required for process monitoring)

### Quick Start
1. Download `SaveTracker.exe` from [Releases](../../releases)
2. Run the executable (self-contained, no installation needed)
3. Grant administrator privileges when prompted

---

## 🚀 Usage Guide

### First Time Setup
1. **Add Games**
   - Click "Add Game Manually" or let the auto-detector scan your library
   - Select your game's executable (`.exe`)
   - Installation paths are automatically resolved

2. **Configure Cloud**
   - Navigate to "☁️ Cloud" settings
   - Choose your provider (Google Drive, OneDrive, etc.)
   - Authenticate securely via browser

3. **Start Playing**
   - Launch games directly from SaveTracker or your favorite launcher
   - SaveTracker detects the process and begins monitoring automatically
   - Saves are synced to the cloud immediately upon game exit

### Headless Mode
For users who prefer a silent background experience, **SaveTracker.Headless** offers the same powerful tracking engine without the UI overhead. Ideally suited for system startup or integration with other launchers.

### IPC Integration
Developers and power users can interact with SaveTracker programmatically using the built-in IPC (Inter-Process Communication) API.
- **Protocol**: Named Pipes (JSON-RPC)
- **Capabilities**: Query game status, trigger syncs, manage library, and more.
- **Example**: The official SaveTracker Playnite extension uses this API for seamless integration.

---

## 🔧 Advanced Configuration

Access via **⚙️ Settings**:

- **Smart Sync**: Optimize bandwidth by only uploading changed files (uses advanced checksumming)
- **Blacklist**: Exclude specific files or folders from tracking
- **Start Location**: Option to start minimized to tray
- **Debug Console**: View real-time logs for troubleshooting

**Data Locations:**
- Config: `{AppDirectory}\Data\config.json`
- Database: `{AppDirectory}\Data\gameslist.json`
- Logs: `{AppDirectory}\Logs\`

---

## 🛠️ Building from Source

**Prerequisites:**
- .NET 8.0 SDK

**Build Command:**
```powershell
dotnet publish SaveTracker/SaveTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

**Note:** The project uses `PublishTrimmed` and `SingleFile` deployment for maximum efficiency. `ReadyToRun` is currently disabled to ensure build stability.

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details

---

## 🙏 Credits

- [Avalonia UI](https://avaloniaui.net/) - The powerful cross-platform UI framework
- [Rclone](https://rclone.org/) - The backbone of our cloud synchronization
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - Essential MVVM components

---

## 📋 Roadmap

### Current (v0.5.0 Beta)
- ✅ Core save tracking
- ✅ Cloud sync
- ✅ Auto-updater
- ✅ Multi-cloud support
- ✅ Batch upload optimization
- ✅ Privacy-focused analytics

### Planned for v1.0
- [ ] Extended user testing
- [ ] Bug fixes from community feedback
- [ ] Performance improvements
- [ ] Better error handling

### Future Features
- [ ] Linux/macOS support
- [ ] Backup versioning
- [ ] Save file compression
- [ ] Scheduled backups
- [ ] Import/Export configurations

---

## 📞 Support

- **Issues:** [GitHub Issues](../../issues)
- **Email:** kooorajoj@gmail.com

---

<div align="center">

**Made with ❤️ for gamers who value their progress**

*Evolved from a Playnite plugin to support all game launchers*

</div>
