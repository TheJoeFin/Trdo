# Changelog

All notable changes to Traydio (formerly Trdo) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - Unreleased

### Changed
- **Renamed to Traydio** — same app, clearer name (#53)
- The tray flyout is now a regular window placed near the click position, fixing DPI and reliability issues with the old flyout (#98, #96, #42)
- LibVLC is now the default playback engine, with Windows Media Foundation kept as a fallback and as an opt-in "Native" mode (broader codec and stream support)

### Added
- "Allow PC to sleep" setting so the computer can sleep on its normal schedule while radio is playing (#82)
- "Native preferred" playback engine mode preserving the pre-2.0 native-first behavior

## [1.1.0] - 2025

### Added
- System tray integration for background playback
- Multiple radio station support
- Stream watchdog for automatic reconnection
- Start with Windows option
- Volume control with persistent settings
- Modern Fluent Design UI
- About page with app information
- Settings page for customization

### Features
- Add/Edit/Remove radio stations
- Station list with selection indicators
- Automatic stream recovery on connection loss
- Theme-aware tray icons
- Persistent volume and station preferences

## [1.0.0] - 2025 (Beta)

### Added
- Initial release
- Basic radio streaming functionality
- WinUI 3 interface
- .NET 9 support

---

## Version History Legend

- **Added** for new features
- **Changed** for changes in existing functionality
- **Deprecated** for soon-to-be removed features
- **Removed** for now removed features
- **Fixed** for any bug fixes
- **Security** for vulnerability fixes
