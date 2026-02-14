# 🎮 SaveTracker Desktop

> **Preserve Your Legacy.**
> *Automatic, cross-platform game save synchronization for the modern gamer.*

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3.9-8B44AC)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![Experimental](https://img.shields.io/badge/Linux-Experimental-orange?logo=linux)](https://www.linux.org/)

**SaveTracker Desktop** isn't just a backup tool—it's your game progression insurance. Built to run silently and efficiently, it watches over your save files like a guardian, instantly syncing them to your personal cloud the moment you stop playing. Whether you're on Steam, Epic, GOG, or rocking a DRM-free library, SaveTracker ensures your hours of gameplay are never lost to a corrupted drive or accidental deletion.

🚀 **Beta Release v0.5.0**

---

## 🔥 Why SaveTracker?

### 🧠 **It Just Knows.**
Forget manual uploads. SaveTracker's **Smart Tracking Engine** detects when a game launches and watches the filesystem in real-time. The second you quit, your progress is safely in the cloud.

### ⚡ **Native Speed.**
Written in performance-obsessed **.NET 8 AOT**, SaveTracker starts instantly and stays out of your way. No bloated frameworks, no heavy resource usage—just raw efficiency.

### � **Partial Linux Support (Experimental)**
We love the Penguin!
- **Headless Power**: Run SaveTracker as a background daemon on your Linux rig or Steam Deck.
- **Process Monitoring**: Native tracking for Linux processes via `/proc`.
- *Note: GUI is currently Windows-optimized, but the core engine is cross-platform ready.*

### ☁️ **Your Cloud, Your Rules.**
We don't hold your data hostage. Sync directly to your own storage:
- **Google Drive**
- **OneDrive**
- **Dropbox**
- **Nextcloud**
- **pCloud**
- *...and 10+ more via [Rclone](https://rclone.org/) integration.*

---

## 💎 Feature Highlights

- **Headless Mode**: A phantom process for tracking without a UI. Perfect for HTPCs and handhelds.
- **Smart Sync**: Uploads only what changed. Advanced checksumming saves your bandwidth.
- **IPC API**: Developers can hook into SaveTracker via simple JSON-RPC pipes.
- **Universal Launcher**: Works with everything. Yes, even that obscure indie game from 2005.
- **Privacy First**: No accounts, no servers, no tracking. Your data stays yours.

---

## ⚡ Quick Start

### Windows
1. **Grab the Binary**: Download `SaveTracker.exe` from [Releases](../../releases).
2. **Run It**: No installer needed. It's portable.
3. **Add a Game**: Point it to an executable, and we'll handle the rest.
4. **Connect Cloud**: OAuth into your provider of choice. Done.

### Linux (Advanced Users)
SaveTracker's core logic is portable! While the UI is Windows-first, you can build and run the core components on Linux.
> *Experimental: Expect some rough edges. Contributions welcome!*

---

## 🛠️ For Developers

Want to build it yourself?
```bash
git clone https://github.com/KrachDev/SaveTrackerDesktop.git
cd SaveTrackerDesktop
dotnet restore
# Build the beast
dotnet publish SaveTracker/SaveTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Tech Stack:**
- **Core**: .NET 8, C# 12
- **UI**: Avalonia UI (Fluent Theme)
- **Sync**: Embedded Rclone
- **Architecture**: MVVM, Native AOT, IPC

---

## 🤝 Join the Mission

We're building the ultimate save manager, and we need you.
- **Found a bug?** [Open an Issue](../../issues)
- **Have an idea?** [Start a Discussion](../../discussions)
- **Code wizard?** Submit a [Pull Request](../../pulls)

---

## � License

MIT License. Free forever. Open source always.

---

<div align="center">

**Project Status: Active Beta**
*Evolved from a Playnite plugin. Built for the future.*

</div>
