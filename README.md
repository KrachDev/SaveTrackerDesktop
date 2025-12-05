# 🎮 SaveTracker Desktop

> **Version 0.4.1 Beta** - Automatic Game Save File Tracking & Cloud Sync

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3.9-8B44AC)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows)](https://www.microsoft.com/windows)

**SaveTracker Desktop** is a standalone application that automatically tracks and synchronizes your game save files to the cloud. Originally developed as a Playnite plugin, it evolved into a desktop app to support multiple game launchers (Steam, Epic, GOG, etc.) with better flexibility and control.

⚠️ **This is beta software** - Please report any issues you encounter!

---

## ✨ Key Features

- 🔄 **Automatic Save Tracking** - Monitors game processes and tracks file changes during gameplay
- ☁️ **Multi-Cloud Support** - Sync to Google Drive, OneDrive, Dropbox, and 7+ other providers
- 🚀 **Auto-Updater** - Automatically checks for and installs updates
- 🎮 **Multi-Launcher Support** - Works with Steam, Epic, GOG, and any game launcher
- 📁 **Manual File Management** - Add/remove tracked files with an intuitive interface
- ⚡ **Smart Sync** - Only uploads modified files to save bandwidth
- 🔍 **Auto-Detection** - Detects running games and offers to track them
- 🎨 **Modern UI** - Dark theme with clean, professional design

---

## 📥 Installation

### Requirements
- Windows 10/11 (64-bit)
- Administrator rights (for process monitoring)

### Quick Start
1. Download `SaveTracker.exe` from [Releases](../../releases)
2. Run the executable (self-contained, no installation needed)
3. Grant administrator privileges when prompted

---

## 🚀 Quick Guide

### First Time Setup
1. **Add a Game**
   - Click "Add Game Manually"
   - Browse to your game's `.exe` file
   - Installation directory is auto-detected

2. **Configure Cloud Storage**
   - Click "☁️ Cloud" in the menu
   - Select your cloud provider
   - Authenticate in your browser

3. **Start Tracking**
   - Select a game from the list
   - Click "Launch & Track" to start the game
   - Play normally - saves are tracked automatically
   - Files sync to cloud when you exit the game

### Cloud Providers Supported
- Google Drive
- OneDrive  
- Dropbox
- pCloud
- Box
- Amazon Drive
- Yandex Disk
- Put.io
- HiDrive
- Uptobox

*Powered by [Rclone](https://rclone.org/)*

---

## 🔧 Settings

Access via **⚙️ Settings** menu:

- **Enable Automatic Tracking** - Auto-detect running games
- **Start with Windows** - Launch on system startup
- **Start Minimized** - Start in system tray
- **Show Debug Console** - Display technical logs
- **Check for updates on startup** - Auto-updater feature

---

## 🛠️ Technical Details

**Built with:**
- .NET 8.0
- Avalonia UI 11.3.9
- Rclone (embedded for cloud sync)
- Windows Process Monitoring (WMI)

**Data Storage:**
- Config: `{AppDirectory}\Data\config.json`
- Game List: `{AppDirectory}\Data\gameslist.json`
- Rclone Config: `{AppDirectory}\ExtraTools\rclone.conf`

---

## 🐛 Known Issues & Limitations

This is **beta software**. Known limitations:
- Windows only (Linux/macOS planned for future)
- Requires administrator privileges
- Limited user testing
- Some edge cases may not be handled

**Report bugs:** [GitHub Issues](../../issues)

---

## 📋 Roadmap

### Current (v0.3.0 Beta)
- ✅ Core save tracking
- ✅ Cloud sync
- ✅ Auto-updater
- ✅ Multi-cloud support

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

## 🤝 Contributing

Contributions welcome! This project is in active development.

**Building from source:**
```bash
git clone https://github.com/KrachDev/SaveTrackerDesktop.git
cd SaveTrackerDesktop
dotnet restore
dotnet build
dotnet run --project SaveTracker/SaveTracker.csproj
```

**Publishing:**
```bash
dotnet publish SaveTracker/SaveTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

---

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details

---

## 🙏 Credits

- [Avalonia UI](https://avaloniaui.net/) - Cross-platform UI framework
- [Rclone](https://rclone.org/) - Cloud storage sync
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM helpers

---

## 📞 Support

- **Issues:** [GitHub Issues](../../issues)
- **Email:** kooorajoj@gmail.com

---

<div align="center">

**Made with ❤️ for gamers who never want to lose their progress**

*Evolved from a Playnite plugin to support all game launchers*

</div>
