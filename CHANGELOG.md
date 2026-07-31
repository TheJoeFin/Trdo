# Changelog

All notable changes to Traydio (formerly Trdo) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - Unreleased

### Changed
- **Renamed to Traydio** — same app, clearer name (#53)
- The tray flyout is now a regular window placed near the click position, fixing DPI and reliability issues with the old flyout (#98, #96, #42)
- LibVLC is now the default playback engine, with Windows Media Foundation kept as a fallback and as an opt-in "Native" mode (broader codec and stream support)
- Volume is now shown as a percentage from 0% to 200%, with 100% (full stream volume) as the default; values above 100% amplify the stream on the LibVLC engine (#105)
- Volume is now remembered per station, so each station keeps its own level and restores it when selected (#16)

### Added
- Optional song change popup: a brief notification above the taskbar whenever the playing song changes, off by default and enabled in Settings
- Song popup delay: many stations announce a track before it actually starts, so the popup can now be held back until it lines up with the audio. Set an app-wide default in Settings, override it per station in the station's Advanced settings, or right-click the popup itself to adjust the station that's playing. Left-clicking the popup dismisses it
- "Allow PC to sleep" setting so the computer can sleep on its normal schedule while radio is playing (#82)
- "Native preferred" playback engine mode preserving the pre-2.0 native-first behavior
- Network awareness: playback is not attempted when offline, and a station that fails because there is no internet connection now shows an in-app error (#102)
- Clearer playback error messages when a stream fails to start — the underlying stream/source error is surfaced, and repeated failures now report "couldn't play after several attempts" instead of retrying silently forever (#103)
- Automatic engine fallback on play: if an engine accepts a stream but never actually produces audio, Traydio now detects the stall within seconds, switches to the other engine and keeps playing, instead of leaving a silent player with no error
- Per-station engine memory: Traydio remembers which engine each station actually plays on (and which one fails) and starts there next time, with a "Reset remembered engines" button in Settings
- Stream diagnosis on failure: when a station won't play, Traydio probes the stream and explains why — server offline, address not found, HTTP error, or the address being a playlist file rather than a stream (in which case it names the URL to use instead)
- LibVLC's own log is now captured into the app log, so a LibVLC failure records the actual reason rather than a generic "LibVLC playback error"; Media Foundation failures now include a decoded HRESULT
- Log entries keep the stream's port and path (query strings are still redacted), so stations sharing a host can be told apart when diagnosing a problem

### Fixed
- "Copy diagnostics" now copies the current log. It was unable to open the active log file at all (a file-sharing mismatch with the log writer) and silently fell back to the previous rolled-over generation, and it did not wait for buffered lines to reach disk — so the most recent entries, the ones worth reporting, were the ones most likely to be missing

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
