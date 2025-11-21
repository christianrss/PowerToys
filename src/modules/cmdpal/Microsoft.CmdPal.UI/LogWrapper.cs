// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Runtime.InteropServices;
using ManagedCommon;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Microsoft.CmdPal.UI;

internal class LogWrapper : ILogger
{
    private static readonly object _lock = new();
    private static readonly AsyncLocal<Stack<object>> _scopes = new();
    private static readonly LogLevel _minLevel = InitializeLevel();

    public string CurrentVersionLogDirectoryPath => Logger.CurrentVersionLogDirectoryPath;

    public LogWrapper()
    {
        try
        {
            Logger.InitializeLogger("\\CmdPal\\Logs\\");
        }
        catch (COMException e)
        {
            // This is unexpected. For the sake of debugging:
            // pop a message box
            PInvoke.MessageBox(
                (HWND)IntPtr.Zero,
                $"Failed to initialize the logger. COMException: \r{e.Message}",
                "Command Palette",
                MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
        }
        catch (Exception e2)
        {
            // This is unexpected. For the sake of debugging:
            // pop a message box
            PInvoke.MessageBox(
                (HWND)IntPtr.Zero,
                $"Failed to initialize the logger. Unknown Exception: \r{e2.Message}",
                "Command Palette",
                MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
        }
    }

    private static LogLevel InitializeLevel()
    {
        var env = Environment.GetEnvironmentVariable("COMMANDPALETTE_LOG_LEVEL");
        if (Enum.TryParse<LogLevel>(env, ignoreCase: true, out var level))
        {
            return level;
        }
#if DEBUG
        return LogLevel.Debug;
#else
        return LogLevel.Information;
#endif
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        var stack = _scopes.Value;
        if (stack is null)
        {
            stack = new Stack<object>();
            _scopes.Value = stack;
        }

        stack.Push(state);
        return new Scope(stack);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return; // Nothing to log.
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var level = logLevel.ToString().ToUpperInvariant();

        var scopeText = string.Empty;
        var stack = _scopes.Value;
        if (stack is not null && stack.Count > 0)
        {
            scopeText = $" [Scopes: {string.Join(" => ", stack.ToArray())}]";
        }

        var eventText = eventId.Id != 0 ? $" (EventId: {eventId.Id}/{eventId.Name})" : string.Empty;
        var line = $"{timestamp} [{level}]{eventText}{scopeText} {message}";

        lock (_lock)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Console.Error.WriteLine(line);
                if (exception is not null)
                {
                    Console.Error.WriteLine(exception);
                }
            }
            else
            {
                Console.Out.WriteLine(line);
                if (exception is not null)
                {
                    Console.Out.WriteLine(exception);
                }
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Stack<object> _stack;
        private bool _disposed;

        public Scope(Stack<object> stack) => _stack = stack;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_stack.Count > 0)
            {
                _stack.Pop();
            }

            _disposed = true;
        }
    }
}
