# 🎮 SaveTracker Desktop

> **Version 1.0.0** - Your Ultimate Game Save File Management & Cloud Sync Solution

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3.9-8B44AC)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

SaveTracker Desktop is a powerful, modern desktop application that automatically tracks, manages, and synchronizes your game save files to the cloud. Never lose your game progress again!

---

## ✨ Features

### 🎯 Core Functionality

- **🔄 Automatic Save Tracking** - Monitors game processes and automatically tracks file changes during gameplay
- **☁️ Multi-Cloud Support** - Sync your saves to 9+ popular cloud storage providers
- **📁 Manual File Management** - Add, remove, and manage tracked files with an intuitive interface
- **🚀 Launch & Track** - Launch games directly from SaveTracker with automatic tracking enabled
- **⚡ Real-time Sync** - Instant upload of modified save files when you close your game
- **📊 File Status Monitoring** - Visual indicators for file status, size, and last modified date
- **🔍 Smart Detection** - Automatically detects running games and offers to track them

### 🎨 User Interface

- **Modern Dark Theme** - Sleek, professional interface with Visual Studio Code-inspired design
- **Game Library** - Visual game list with icons and installation paths
- **Dual-Tab View** - Separate views for local tracked files and cloud storage
- **Status Bar** - Real-time sync status and statistics
- **Settings Panel** - Comprehensive configuration options

### ☁️ Supported Cloud Providers

SaveTracker supports the following cloud storage services:

- ✅ **Google Drive**
- ✅ **OneDrive**
- ✅ **Dropbox**
- ✅ **pCloud**
- ✅ **Box**
- ✅ **Amazon Drive**
- ✅ **Yandex Disk**
- ✅ **Put.io**
- ✅ **HiDrive**
- ✅ **Uptobox**

*Powered by [Rclone](https://rclone.org/) for reliable cloud synchronization*

---

## 📥 Installation

### System Requirements

- **Operating System:** Windows 10/11 (64-bit)
- **RAM:** 512 MB minimum
- **Disk Space:** 150 MB
- **Administrator Rights:** Required for process monitoring

### Quick Install

1. **Download** the latest release from the [Releases](../../releases) page
2. **Extract** `SaveTracker.exe` from the archive
3. **Run** `SaveTracker.exe` (no installation required!)
4. **Grant** administrator privileges when prompted

> **Note:** SaveTracker is a self-contained application - no .NET runtime installation needed!

---

## 🚀 Getting Started

### First Launch

1. **Launch SaveTracker** - The application will request administrator privileges
2. **Add Your First Game:**
   - Click **"+ Add Game Manually"** in the sidebar
   - Browse to your game's executable
   - Select the game's installation directory
3. **Configure Cloud Storage:**
   - Click **☁️ Cloud** in the top menu
   - Select your preferred cloud provider
   - Follow the OAuth authentication flow in your browser
4. **Start Tracking:**
   - Select a game from the list
   - Click **"▶️ Launch & Track"** to start the game with automatic tracking
   - Or manually add files using **"➕ Add Files"**

### Basic Workflow

```
1. Add Game → 2. Configure Cloud → 3. Launch & Track → 4. Play Game → 5. Auto-Sync on Exit
```

---

## 📖 User Guide

### Adding Games

**Method 1: Manual Addition**
- Click **"+ Add Game Manually"**
- Browse to the game's `.exe` file
- The installation directory is auto-detected

**Method 2: Automatic Detection**
- Enable **"Automatic Tracking"** in settings
- Launch any game normally
- SaveTracker will detect it and offer to track it

### Managing Tracked Files

**Adding Files:**
1. Select a game from the list
2. Click **"➕ Add Files"** in the Tracked Files tab
3. Select save files from your game directory
4. Files are immediately tracked and checksummed

**Removing Files:**
1. Check the boxes next to files you want to remove
2. Click **"🗑️ Remove Selected"**

**Viewing File Details:**
- Double-click any tracked file to open its location in Explorer

### Cloud Synchronization

**Initial Setup:**
1. Click **☁️ Cloud** in the menu bar
2. Select your cloud provider from the dropdown
3. Click **"Configure"**
4. Authenticate in your browser
5. Grant SaveTracker access to your cloud storage

**Manual Sync:**
- Click **"🔄 Sync Now"** to manually upload current save files

**Automatic Sync:**
- Enabled by default
- Saves are automatically uploaded when you close a tracked game
- Toggle with the **"Auto-upload"** checkbox

**Downloading Saves:**
1. Switch to the **"☁️ Cloud Folder"** tab
2. Click **"🔄 Refresh"** to load cloud files
3. Select files to download
4. Click **"⬇️ Download Selected"**

### Settings

Access settings via **⚙️ Settings** in the menu bar:

| Setting | Description | Default |
|---------|-------------|---------|
| **Enable Automatic Tracking** | Detect and offer to track running games | ✅ On |
| **Start with Windows** | Launch SaveTracker on system startup | ❌ Off |
| **Start Minimized** | Start in system tray | ❌ Off |
| **Show Debug Console** | Display technical logging console | ❌ Off |

---

## 🔧 Advanced Features

### Blacklist Management

Prevent certain files from being tracked:
1. Click **⛔ Blacklist** in the menu
2. Add file patterns or extensions to exclude
3. Useful for temporary files, logs, or cache files

### Process Monitoring

SaveTracker uses advanced Windows process monitoring to:
- Detect when games start and stop
- Track file access patterns during gameplay
- Identify which files were modified
- Automatically trigger uploads on game exit

### File Checksumming

- All tracked files are checksummed using MD5
- Only modified files are uploaded (saves bandwidth)
- Prevents unnecessary cloud storage operations

---

## 🛠️ Technical Details

### Architecture

- **Framework:** .NET 8.0
- **UI Framework:** Avalonia 11.3.9 (Cross-platform XAML)
- **MVVM Toolkit:** CommunityToolkit.Mvvm 8.4.0
- **Cloud Sync:** Rclone (embedded)
- **Process Monitoring:** System.Management (WMI)

### Project Structure

```
SaveTracker/
├── Views/              # UI Windows and Dialogs
├── ViewModels/         # MVVM ViewModels
├── Resources/
│   ├── HELPERS/        # Utility classes
│   ├── LOGIC/          # Business logic
│   │   └── RecloneManagement/  # Cloud sync logic
│   └── SAVE_SYSTEM/    # Save file management
└── Assets/             # Icons and resources
```

### Data Storage

SaveTracker stores data in:
- **Game Configurations:** `%AppData%\SaveTracker\Games\`
- **Tracked Files Data:** `%AppData%\SaveTracker\GameData\`
- **Application Config:** `%AppData%\SaveTracker\config.json`
- **Rclone Config:** `ExtraTools\rclone.conf`

---

## 🐛 Troubleshooting

### Common Issues

**"Administrator privileges required" error**
- SaveTracker needs admin rights to monitor game processes
- Right-click `SaveTracker.exe` → Run as Administrator
- Or click "Yes" when prompted

**Cloud sync not working**
- Verify cloud provider is configured: **☁️ Cloud** → **Configure**
- Check internet connection
- Re-authenticate with your cloud provider

**Game not detected automatically**
- Ensure "Enable Automatic Tracking" is enabled in settings
- Check that the game is in your game list
- Try adding the game manually first

**Files not uploading**
- Verify "Auto-upload" is checked in Tracked Files tab
- Check that files were modified during gameplay
- Review debug console for errors (enable in settings)

### Debug Mode

Enable debug console for detailed logging:
1. Go to **⚙️ Settings**
2. Enable **"Show Debug Console"**
3. Check console output for errors and warnings

---

## 🤝 Contributing

Contributions are welcome! Here's how you can help:

1. **Report Bugs:** Open an issue with detailed reproduction steps
2. **Suggest Features:** Share your ideas in the issues section
3. **Submit PRs:** Fork, create a feature branch, and submit a pull request

### Building from Source

```bash
# Clone the repository
git clone https://github.com/YourUsername/SaveTrackerDesktop.git
cd SaveTrackerDesktop

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project SaveTracker/SaveTracker.csproj

# Publish for distribution
dotnet publish SaveTracker/SaveTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

---

## 📋 Roadmap

### Planned Features

- [ ] **Multi-platform Support** - Linux and macOS versions
- [ ] **Game Profiles** - Different tracking configurations per game
- [ ] **Backup Versioning** - Keep multiple versions of save files
- [ ] **Scheduled Backups** - Automatic periodic backups
- [ ] **Import/Export** - Share game configurations
- [ ] **Cloud Storage Quota Display** - Monitor storage usage
- [ ] **Compression** - Compress saves before upload
- [ ] **Encryption** - Encrypt saves for privacy

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **[Avalonia UI](https://avaloniaui.net/)** - Cross-platform XAML framework
- **[Rclone](https://rclone.org/)** - Cloud storage sync engine
- **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** - MVVM helpers
- **[MessageBox.Avalonia](https://github.com/AvaloniaCommunity/MessageBox.Avalonia)** - Dialog library

---

## 📞 Support

- **Issues:** [GitHub Issues](../../issues)
- **Discussions:** [GitHub Discussions](../../discussions)
- **Email:** your.email@example.com

---

## 📊 Statistics

![GitHub release (latest by date)](https://img.shields.io/github/v/release/YourUsername/SaveTrackerDesktop)
![GitHub all releases](https://img.shields.io/github/downloads/YourUsername/SaveTrackerDesktop/total)
![GitHub stars](https://img.shields.io/github/stars/YourUsername/SaveTrackerDesktop)

---

<div align="center">

**Made with ❤️ for gamers who never want to lose their progress**

[⬆ Back to Top](#-savetracker-desktop)

</div>
