using Microsoft.Windows.Widgets.Providers;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Trdo.Widgets;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("D5A5B8F2-9C3A-4E1B-8F7D-6A4C3B2E1D9F")]
public sealed class TrdoWidgetProvider : IWidgetProvider
{
    public TrdoWidgetProvider()
    {
        RecoverRunningWidgets();
    }

    private static bool HaveRecoveredWidgets { get; set; } = false;
    private static void RecoverRunningWidgets()
    {
        if (!HaveRecoveredWidgets)
        {
            try
            {
                var widgetManager = WidgetManager.GetDefault();
                foreach (var widgetInfo in widgetManager.GetWidgetInfos())
                {
                    var context = widgetInfo.WidgetContext;
                    if (!WidgetInstances.ContainsKey(context.Id))
                    {
                        if (WidgetImpls.ContainsKey(context.DefinitionId))
                        {
                            // Need to recover this instance
                            WidgetInstances[context.Id] = WidgetImpls[context.DefinitionId](context.Id, widgetInfo.CustomState);
                        }
                        else
                        {
                            // this provider doesn't know about this type of Widget (any more?) delete it
                            widgetManager.DeleteWidget(context.Id);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle recovery errors
            }
            finally
            {
                HaveRecoveredWidgets = true;
            }
        }
    }

    private static readonly Dictionary<string, WidgetCreateDelegate> WidgetImpls = new() {
        [RadioPlayerWidget.DefinitionId] = (widgetId, initialState) => new RadioPlayerWidget(widgetId, initialState)
    };

    private static Dictionary<string, WidgetImplBase> WidgetInstances = new();

    public void CreateWidget(WidgetContext widgetContext)
    {
        if (!WidgetImpls.ContainsKey(widgetContext.DefinitionId))
        {
            throw new Exception($"Invalid definition requested: {widgetContext.DefinitionId}");
        }

        var widgetInstance = WidgetImpls[widgetContext.DefinitionId](widgetContext.Id, "");
        WidgetInstances[widgetContext.Id] = widgetInstance;

        WidgetUpdateRequestOptions options = new WidgetUpdateRequestOptions(widgetContext.Id);
        options.Template = widgetInstance.GetTemplateForWidget();
        options.Data = widgetInstance.GetDataForWidget();
        options.CustomState = widgetInstance.State;

        WidgetManager.GetDefault().UpdateWidget(options);
    }

    public void DeleteWidget(string widgetId, string _)
    {
        WidgetInstances.Remove(widgetId);
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        if (WidgetInstances.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var widget))
        {
            widget.OnActionInvoked(actionInvokedArgs);
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        if (WidgetInstances.TryGetValue(contextChangedArgs.WidgetContext.Id, out var widget))
        {
            widget.OnWidgetContextChanged(contextChangedArgs);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        if (WidgetInstances.TryGetValue(widgetContext.Id, out var widget))
        {
            widget.Activate(widgetContext);
        }
    }

    public void Deactivate(string widgetId)
    {
        if (WidgetInstances.TryGetValue(widgetId, out var widget))
        {
            widget.Deactivate();
        }
    }
}
