# How to Use Trdo Widgets

## Adding the Widget to Windows 11

1. **Install Trdo** from the Microsoft Store or build from source
2. **Open the Widgets Panel**:
   - Click the Widgets icon in the Windows 11 taskbar, or
   - Press `Win + W` on your keyboard
3. **Add the Trdo Widget**:
   - Click the "+" button to add a widget
   - Search for "Trdo" in the widget picker
   - Select "Trdo Radio Player" widget
   - Click "Pin" to add it to your widgets panel

## Using the Widget

The Trdo widget displays:
- **App Name**: "Trdo" at the top
- **Status**: Shows "Now Playing" when a station is playing, or "Paused" when stopped
- **Station Name**: The currently selected radio station (or "No station selected" if none)
- **Control Button**: 
  - Shows "▶ Play" when paused - click to start playback
  - Shows "⏸ Pause" when playing - click to pause playback

## Widget Behavior

### Automatic Updates
The widget automatically updates when:
- You start or stop playback from the main Trdo app
- You change the selected radio station
- You interact with the widget button

### Widget Sizes
The widget supports three sizes:
- **Small**: Compact view with essential information
- **Medium**: Standard view (recommended)
- **Large**: Expanded view with more space

To resize the widget:
1. Right-click on the widget in the Widgets panel
2. Select a different size from the context menu

### Background Behavior
- The widget works even when the main Trdo app is minimized to the system tray
- Widget updates happen in real-time without user intervention
- The widget provider runs as a background process when widgets are active

## Troubleshooting

### Widget Not Appearing
If the Trdo widget doesn't appear in the widget picker:
1. Ensure Trdo is properly installed
2. Restart Windows
3. Check that Windows 11 Widgets are enabled in Windows Settings

### Widget Not Updating
If the widget doesn't update when you change playback:
1. Remove and re-add the widget
2. Restart the Trdo app
3. Check that the main app is running (look for the tray icon)

### Widget Shows "No station selected"
This is normal when:
- You haven't added any radio stations yet
- No station is currently selected
- The app is starting up

To fix:
1. Open Trdo from the system tray
2. Add a radio station
3. Select the station to start playback

## Privacy and Performance

- **Data**: The widget only displays data from your local Trdo app - no external data is collected
- **Performance**: The widget uses minimal system resources
- **Background Process**: When widgets are active, a lightweight background process runs to handle widget updates
- **No Telemetry**: Widget interactions are not tracked or sent anywhere

## Tips

- **Quick Access**: Pin the widget to quickly control playback without opening the app
- **Multiple Widgets**: You can add multiple Trdo widgets if desired (they all show the same information)
- **Tray Integration**: The widget complements the system tray icon for easy access from anywhere
- **Startup**: If Trdo is set to start on Windows startup, widgets will work immediately after login

## Known Limitations

- Widget only shows one station at a time (the currently selected station)
- Cannot switch between stations directly from the widget
- No volume control in the widget (use the main app or system volume)
- Widget requires Windows 11 (widgets are not available on Windows 10)

## Feedback

If you encounter issues or have suggestions for the widget feature, please:
- Open an issue on the [Trdo GitHub repository](https://github.com/TheJoeFin/Trdo/issues)
- Contact the developer on Twitter [@TheJoeFin](https://twitter.com/thejoefin)
