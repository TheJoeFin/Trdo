using Microsoft.Windows.Widgets.Providers;
using System;
using System.IO;
using Windows.Storage;

namespace Trdo.Widgets;

internal delegate WidgetImplBase WidgetCreateDelegate(string widgetId, string initialState);

internal abstract class WidgetImplBase
{
    protected string state = string.Empty;
    protected bool isActivated = false;

    public string Id { get; private set; }

    public string State
    {
        get => state;
    }

    public WidgetImplBase(string widgetId, string initialState)
    {
        Id = widgetId;
        state = initialState;
    }

    public abstract void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs);
    public abstract void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs);
    public abstract void Activate(WidgetContext widgetContext);
    public abstract void Deactivate();
    public abstract string GetTemplateForWidget();
    public abstract string GetDataForWidget();

    protected static string ReadPackageFileFromUri(string uri)
    {
        try
        {
            Uri resourceUri = new Uri(uri);
            StorageFile file = StorageFile.GetFileFromApplicationUriAsync(resourceUri).GetAwaiter().GetResult();
            return FileIO.ReadTextAsync(file).GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
    }
}
