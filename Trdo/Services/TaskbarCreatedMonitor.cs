using Microsoft.UI.Dispatching;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Trdo.Services;

internal sealed partial class TaskbarCreatedMonitor : IDisposable
{
    private delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    private const string WindowClassPrefix = "Trdo.TaskbarCreatedMonitorWindow.";
    private static readonly ConcurrentDictionary<nint, TaskbarCreatedMonitor> s_monitors = new();
    private static readonly WindowProc s_windowProc = WindowProcedure;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly nint _windowHandle;
    private readonly nint _moduleHandle;
    private readonly string _windowClassName;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public event EventHandler? TaskbarCreated;

    public TaskbarCreatedMonitor()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("TaskbarCreatedMonitor must be created on a thread with a DispatcherQueue.");

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        _moduleHandle = GetModuleHandle(null);
        if (_moduleHandle == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        _windowClassName = $"{WindowClassPrefix}{Guid.NewGuid():N}";

        WNDCLASSEX windowClass = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            hInstance = _moduleHandle,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_windowProc),
            lpszClassName = _windowClassName
        };

        if (RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        _windowHandle = CreateWindowEx(
            0,
            _windowClassName,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            _moduleHandle,
            0);

        if (_windowHandle == 0)
        {
            _ = UnregisterClass(_windowClassName, _moduleHandle);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        s_monitors[_windowHandle] = this;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_windowHandle != 0)
        {
            s_monitors.TryRemove(_windowHandle, out _);
            _ = DestroyWindow(_windowHandle);
        }

        _ = UnregisterClass(_windowClassName, _moduleHandle);
        GC.SuppressFinalize(this);
    }

    private void HandleWindowMessage(uint message)
    {
        if (message != _taskbarCreatedMessage)
            return;

        _dispatcherQueue.TryEnqueue(() => TaskbarCreated?.Invoke(this, EventArgs.Empty));
    }

    private static nint WindowProcedure(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (s_monitors.TryGetValue(hWnd, out TaskbarCreatedMonitor? monitor))
            monitor.HandleWindowMessage(msg);

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentHandle,
        nint menuHandle,
        nint instanceHandle,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClass(string className, nint instanceHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }
}
