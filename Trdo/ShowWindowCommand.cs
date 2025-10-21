using Microsoft.UI.Xaml;
using System;
using System.Reflection;
using System.Windows.Input;

namespace Trdo;

public sealed class ShowWindowCommand : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        // Request App to create/show window
        if (Application.Current is App app)
        {
            MethodInfo? method = typeof(App).GetMethod("EnsureWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(app, null);
        }
    }
}

internal static class WindowHelper
{
    private static Window[] _active = [];

    public static Window[] ActiveWindows()
    {
        return _active;
    }

    public static void Track(Window window)
    {
        for (int i = 0; i < _active.Length; i++)
        {
            if (ReferenceEquals(_active[i], window))
                return;
        }
        Window[] newArr = new Window[_active.Length + 1];
        _active.CopyTo(newArr, 0);
        newArr[^1] = window;
        _active = newArr;
        window.Closed += (_, _) => Remove(window);
    }

    private static void Remove(Window window)
    {
        int idx = Array.IndexOf(_active, window);
        if (idx < 0) return;
        if (_active.Length == 1)
        {
            _active = [];
            return;
        }
        Window[] newArr = new Window[_active.Length - 1];
        if (idx > 0) Array.Copy(_active, 0, newArr, 0, idx);
        if (idx < _active.Length - 1) Array.Copy(_active, idx + 1, newArr, idx, _active.Length - idx - 1);
        _active = newArr;
    }
}
