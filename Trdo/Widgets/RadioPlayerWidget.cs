using Microsoft.Windows.Widgets.Providers;
using System.Text.Json.Nodes;
using Trdo.ViewModels;

namespace Trdo.Widgets;

internal class RadioPlayerWidget : WidgetImplBase
{
    public static string DefinitionId { get; } = "Trdo_RadioPlayer_Widget";
    private static string WidgetTemplate { get; set; } = "";
    private readonly PlayerViewModel _playerVm = PlayerViewModel.Shared;

    public RadioPlayerWidget(string widgetId, string startingState) : base(widgetId, startingState)
    {
        // Subscribe to player state changes
        _playerVm.PropertyChanged += (s, e) =>
        {
            if (isActivated && (e.PropertyName == nameof(PlayerViewModel.IsPlaying) || 
                                e.PropertyName == nameof(PlayerViewModel.SelectedStation)))
            {
                UpdateWidget();
            }
        };
    }

    public override void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (actionInvokedArgs.Verb == "toggle")
        {
            _playerVm.Toggle();
            UpdateWidget();
        }
    }

    public override void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        // Widget size changed, update the widget with new template if needed
        UpdateWidget();
    }

    public override void Activate(WidgetContext widgetContext)
    {
        isActivated = true;
        UpdateWidget();
    }

    public override void Deactivate()
    {
        isActivated = false;
    }

    private void UpdateWidget()
    {
        var updateOptions = new WidgetUpdateRequestOptions(Id);
        updateOptions.Data = GetDataForWidget();
        updateOptions.Template = GetTemplateForWidget();
        updateOptions.CustomState = State;
        WidgetManager.GetDefault().UpdateWidget(updateOptions);
    }

    private static string GetDefaultTemplate()
    {
        if (string.IsNullOrEmpty(WidgetTemplate))
        {
            WidgetTemplate = ReadPackageFileFromUri("ms-appx:///Widgets/Templates/RadioPlayerWidgetTemplate.json");
        }

        return WidgetTemplate;
    }

    public override string GetTemplateForWidget()
    {
        return GetDefaultTemplate();
    }

    public override string GetDataForWidget()
    {
        var stationName = _playerVm.SelectedStation?.Name ?? "No station selected";
        var isPlaying = _playerVm.IsPlaying;
        var buttonText = isPlaying ? "⏸ Pause" : "▶ Play";
        var statusText = isPlaying ? "Now Playing" : "Paused";

        var dataNode = new JsonObject
        {
            ["stationName"] = stationName,
            ["statusText"] = statusText,
            ["buttonText"] = buttonText,
            ["isPlaying"] = isPlaying
        };

        return dataNode.ToJsonString();
    }
}
