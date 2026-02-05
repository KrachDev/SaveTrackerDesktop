# 🎉 SaveTracker v0.4.4 Release Notes

## 🆕 Major Features

### Analytics & Announcements System
- **Opt-Out Analytics**: Analytics are now enabled by default to help improve SaveTracker
  - Completely anonymous - NO personal information collected
  - Track only: device ID (hashed), game names, file counts
  - Easy to disable in Settings → Analytics
- **Version-Based Announcements**: New announcement window shows important updates
  - Appears once per version
  - Shows again when you update to a new version
  - Never miss important changes

### Windows Notification System
- **Native Windows Notifications**: Toast notifications for tracking and upload events
- **In-App Notifications**: Visual feedback when app is in focus
- **System Tray Integration**: Notifications work even when minimized

### Enhanced Search
- **Search Finally Working**: Improved game search functionality
- **Better Filtering**: Find your games faster

### Settings Window Redesign
- **Tabbed Interface**: Clean organization with General, Analytics, and Updates tabs
- **Modern Design**: Matches SaveTracker's aesthetic
- **Better UX**: Easier to find and configure settings

---

## ✨ Improvements

### Performance & Optimization
- **Optimized Game Loading**: Auto-cleanup and parallel validation
- **Faster Startup**: Removed deprecated code and improved efficiency
- **Better Memory Usage**: Cleaned up TrackedLogic.cs

### Cloud & Sync
- **Cloud Library**: Enhanced cloud save management
- **Smart Sync**: Improved synchronization logic
- **Upload Bug Fixes**: Critical fixes for upload reliability

### Game Management
- **Playnite Import**: Import your games from Playnite
- **Per-Game Settings**: Delete button added for easier management
- **PlayTime Tracking**: Record and display play duration
- **Cross-Platform Icons**: Better icon extraction using Ico.Reader

---

## 🐛 Bug Fixes
- Fixed critical upload bugs
- Improved UI state management
- Fixed refresh issues
- Better download logic
- Enhanced save backup validation
- Various stability improvements

---

## 📝 Privacy & Analytics Notice

This release introduces opt-out analytics to help us improve SaveTracker. We collect:
- ✅ Anonymous device ID (SHA256 hash)
- ✅ Game names (no paths)
- ✅ File counts (no file names)

We **DO NOT** collect:
- ❌ Personal information
- ❌ File paths or names
- ❌ Cloud provider details
- ❌ Play duration or timestamps

**You can disable analytics anytime** in Settings → Analytics.

---

Thank you for using SaveTracker! Your feedback helps us make it better. 🎮
