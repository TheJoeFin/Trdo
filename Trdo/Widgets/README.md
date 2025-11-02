# Windows Widget Support for Trdo

This document describes the Windows Widget implementation for Trdo, enabling users to control their radio playback directly from the Windows 11 Widgets panel.

## Overview

Trdo now supports Windows 11 Widgets, allowing users to:
- View the currently playing radio station
- See playback status (Playing/Paused)
- Control playback with a Play/Pause button
- Access Trdo functionality without opening the main application

## Architecture

### Components

1. **Widget Provider (`TrdoWidgetProvider.cs`)**
   - Implements `IWidgetProvider` interface
   - Registered as a COM server for widget activation
   - Manages widget lifecycle (create, delete, activate, deactivate)
   - Handles widget actions and context changes

2. **Radio Player Widget (`RadioPlayerWidget.cs`)**
   - Displays current station and playback status
   - Integrates with the existing `PlayerViewModel`
   - Updates widget UI when playback state changes
   - Handles user interactions (Play/Pause button)

3. **Widget Helper Classes**
   - `WidgetProviderFactory.cs`: COM class factory for creating widget provider instances
   - `RegistrationManager.cs`: Manages COM registration/unregistration lifecycle
   - `WidgetImplBase.cs`: Base class for widget implementations

4. **Widget Template (`RadioPlayerWidgetTemplate.json`)**
   - Adaptive Card JSON defining the widget UI
   - Supports small, medium, and large widget sizes
   - Displays station name, status, and control button

### COM Registration

The widget provider is registered as a COM server in the package manifest:
- CLSID: `D5A5B8F2-9C3A-4E1B-8F7D-6A4C3B2E1D9F`
- Activated when Windows needs to display the widget
- Runs as part of the Trdo executable with `-RegisterProcessAsComServer` argument

## Package Manifest Changes

The `Package.appxmanifest` has been updated with:

1. **COM Server Extension**
   ```xml
   <com:Extension Category="windows.comServer">
     <com:ComServer>
       <com:ExeServer Executable="Trdo.exe" Arguments="-RegisterProcessAsComServer" DisplayName="Trdo Widget Provider">
         <com:Class Id="D5A5B8F2-9C3A-4E1B-8F7D-6A4C3B2E1D9F" DisplayName="Trdo Widget Provider" />
       </com:ExeServer>
     </com:ComServer>
   </com:Extension>
   ```

2. **Widget Provider Extension**
   ```xml
   <uap3:Extension Category="windows.appExtension">
     <uap3:AppExtension Name="com.microsoft.windows.widgets" DisplayName="Trdo Widgets" Id="TrdoWidgets" PublicFolder="Widgets">
       <!-- Widget definitions and configuration -->
     </uap3:AppExtension>
   </uap3:Extension>
   ```

## Application Lifecycle

1. **Normal Launch**: Trdo runs as a tray application
2. **Widget COM Server Launch**: When a widget is added, Windows launches Trdo with `-RegisterProcessAsComServer`
   - COM wrappers are initialized
   - Widget provider is registered
   - App stays running to handle widget requests

## Widget UI

The widget displays:
- **Title**: "Trdo"
- **Status**: "Now Playing" or "Paused"
- **Station Name**: Current radio station or "No station selected"
- **Action Button**: "▶ Play" or "⏸ Pause"

The UI automatically updates when:
- User changes the selected station
- Playback starts or stops
- Widget is activated/deactivated

## Technical Details

### Widget Sizes
The widget supports three sizes:
- Small
- Medium
- Large

All sizes use the same template and adapt based on available space.

### Data Binding
The widget uses Adaptive Card data binding with the following properties:
- `stationName`: Name of the current radio station
- `statusText`: "Now Playing" or "Paused"
- `buttonText`: "▶ Play" or "⏸ Pause"
- `isPlaying`: Boolean flag for playback state

### Integration with PlayerViewModel
The widget subscribes to `PropertyChanged` events from `PlayerViewModel.Shared` to:
- Update when `IsPlaying` changes
- Update when `SelectedStation` changes
- Ensure widget always shows current state

## Assets

Widget assets are located in `Widgets/Assets/`:
- `Widget_Icon.png`: Widget icon (copied from Radio.png)
- `Widget_Screenshot.png`: Widget screenshot for the widgets panel

## Future Enhancements

Potential improvements:
- Show album art or station logo
- Display current song/show information
- Add station selection directly from widget
- Show volume control
- Add favorite stations quick access

## References

- [Microsoft Documentation: Implement a widget provider in C#](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs)
- [Widget Provider Package Manifest](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest)
- [Adaptive Cards Documentation](https://adaptivecards.io/)
