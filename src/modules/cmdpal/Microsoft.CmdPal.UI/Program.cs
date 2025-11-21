// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ManagedCommon;
using Microsoft.CmdPal.Core.Common.Helpers;
using Microsoft.CmdPal.Core.Common.Services;
using Microsoft.CmdPal.Core.ViewModels;
using Microsoft.CmdPal.Ext.Apps;
using Microsoft.CmdPal.Ext.Bookmarks;
using Microsoft.CmdPal.Ext.Calc;
using Microsoft.CmdPal.Ext.ClipboardHistory;
using Microsoft.CmdPal.Ext.Indexer;
using Microsoft.CmdPal.Ext.Registry;
using Microsoft.CmdPal.Ext.Shell;
using Microsoft.CmdPal.Ext.System;
using Microsoft.CmdPal.Ext.TimeDate;
using Microsoft.CmdPal.Ext.WebSearch;
using Microsoft.CmdPal.Ext.WindowsServices;
using Microsoft.CmdPal.Ext.WindowsSettings;
using Microsoft.CmdPal.Ext.WindowsTerminal;
using Microsoft.CmdPal.Ext.WindowWalker;
using Microsoft.CmdPal.Ext.WinGet;
using Microsoft.CmdPal.UI.Events;
using Microsoft.CmdPal.UI.Helpers;
using Microsoft.CmdPal.UI.Pages;
using Microsoft.CmdPal.UI.Services;
using Microsoft.CmdPal.UI.Services.Telemetry;
using Microsoft.CmdPal.UI.Settings;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.CmdPal.UI.ViewModels.BuiltinCommands;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.PowerToys.Telemetry;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Microsoft.CmdPal.UI;

// cribbed heavily from
//
// https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/AppLifecycle/Instancing/cs2/cs-winui-packaged/CsWinUiDesktopInstancing
internal sealed partial class Program
{
    private static readonly ETWTrace _etwTrace = new ETWTrace();
    private static DispatcherQueueSynchronizationContext? uiContext;
    private static App? app;
    private static ILogger _logger = new LogWrapper();
    private static GlobalErrorHandler _globalErrorHandler = new(_logger);
    private static ITelemetryService? _telemetry;

    // LOAD BEARING
    //
    // Main cannot be async. If it is, then the clipboard won't work, and neither will narrator.
    // That means you, the person thinking about making this a MTA thread. Don't
    // do it. It won't work. That's not the solution.
    [STAThread]
    private static int Main(string[] args)
    {
        if (Helpers.GpoValueChecker.GetConfiguredCmdPalEnabledValue() == Helpers.GpoRuleConfiguredValue.Disabled)
        {
            // There's a GPO rule configured disabling CmdPal. Exit as soon as possible.
            return 0;
        }

        ServiceCollection services = new();

        // Root services
        services.AddSingleton(TaskScheduler.FromCurrentSynchronizationContext());
        services.AddSingleton<ILogger>(_logger);
        services.AddSingleton<LocalKeyboardListener>();

        // Settings & state
        var sm = SettingsModel.LoadSettings();
        services.AddSingleton(sm);
        var state = AppStateModel.LoadState();
        services.AddSingleton(state);

        // Services
        services.AddSingleton<IRootPageService, PowerToysRootPageService>();
        services.AddSingleton<IAppHostService, PowerToysAppHostService>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IRunHistoryService, RunHistoryService>();
        services.AddSingleton<TopLevelCommandManager>();
        services.AddSingleton<AliasManager>();
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<TrayIconService>();

        // Built-in Extensions
        var allApps = new AllAppsCommandProvider();
        var files = new IndexerCommandsProvider();
        files.SuppressFallbackWhen(ShellCommandsProvider.SuppressFileFallbackIf);
        services.AddSingleton<ICommandProvider>(allApps);

        services.AddSingleton<ICommandProvider, ShellCommandsProvider>();
        services.AddSingleton<ICommandProvider, CalculatorCommandProvider>();
        services.AddSingleton<ICommandProvider>(files);
        services.AddSingleton<ICommandProvider, BookmarksCommandProvider>(_ => BookmarksCommandProvider.CreateWithDefaultStore());

        services.AddSingleton<ICommandProvider, WindowWalkerCommandsProvider>();
        services.AddSingleton<ICommandProvider, WebSearchCommandsProvider>();
        services.AddSingleton<ICommandProvider, ClipboardHistoryCommandsProvider>();

        // GH #38440: Users might not have WinGet installed! Or they might have
        // a ridiculously old version. Or might be running as admin.
        // We shouldn't explode in the App ctor if we fail to instantiate an
        // instance of PackageManager, which will happen in the static ctor
        // for WinGetStatics
        try
        {
            var winget = new WinGetExtensionCommandsProvider();
            var callback = allApps.LookupApp;
            winget.SetAllLookup(callback);
            services.AddSingleton<ICommandProvider>(winget);
        }
        catch (Exception ex)
        {
            Log_FailureToLoadWinget(_logger!, ex);
        }

        services.AddSingleton<ICommandProvider, WindowsTerminalCommandsProvider>();
        services.AddSingleton<ICommandProvider, WindowsSettingsCommandsProvider>();
        services.AddSingleton<ICommandProvider, RegistryCommandsProvider>();
        services.AddSingleton<ICommandProvider, WindowsServicesCommandsProvider>();
        services.AddSingleton<ICommandProvider, BuiltInsCommandProvider>();
        services.AddSingleton<ICommandProvider, TimeDateCommandsProvider>();
        services.AddSingleton<ICommandProvider, SystemCommandExtensionProvider>();

        // Extensions

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<IPageViewModelFactoryService, CommandPalettePageViewModelFactory>();
        services.AddTransient<SettingsViewModel>();

        // Views
        // App & MainWindow are singletons to ensure only one instance of each exists.
        // Other views can be Transient.
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<GeneralPage>();
        services.AddTransient<ExtensionsPage>();
        services.AddTransient<ExtensionPage>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<ShellPage>();

        var serviceProvider = services.BuildServiceProvider();
        _logger = serviceProvider.GetRequiredService<ILogger>();
        _telemetry = serviceProvider.GetRequiredService<ITelemetryService>();

        if (_logger is not null)
        {
            Log_StartingAt(_logger, DateTime.UtcNow);
        }

        _telemetry.WriteEvent(new ProcessStartedEvent());

        Logger.LogDebug($"Starting at {DateTime.UtcNow}");
        PowerToysTelemetry.Log.WriteEvent(new ProcessStartedEvent());

        // Ensure types used in XAML are preserved for AOT compilation
        TypePreservation.PreserveTypes();

        NativeEventWaiter.WaitForEventLoop(
            "Local\\PowerToysCmdPal-ExitEvent-eb73f6be-3f22-4b36-aee3-62924ba40bfd", () =>
            {
                _etwTrace.Dispose();
                app?.AppWindow?.Close();
                Environment.Exit(0);
            });

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var isRedirect = DecideRedirection();
        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                uiContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(uiContext);
                app = serviceProvider.GetRequiredService<App>();

#if !CMDPAL_DISABLE_GLOBAL_ERROR_HANDLER
                _globalErrorHandler.Register(app);
#endif
            });
        }

        return 0;
    }

    private static bool DecideRedirection()
    {
        var isRedirect = false;
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey("randomKey");

        if (keyInstance.IsCurrent)
        {
            _telemetry?.WriteEvent(new ColdLaunchEvent());
            keyInstance.Activated += OnActivated;
        }
        else
        {
            isRedirect = true;
            _telemetry?.WriteEvent(new ReactivateInstanceEvent());
            RedirectActivationTo(args, keyInstance);
        }

        return isRedirect;
    }

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        // Do the redirection on another thread, and use a non-blocking
        // wait method to wait for the redirection to complete.
        using var redirectSemaphore = new Semaphore(0, 1);
        var redirectTimeout = TimeSpan.FromSeconds(32);

        _ = Task.Run(() =>
        {
            using var cts = new CancellationTokenSource(redirectTimeout);
            try
            {
                keyInstance.RedirectActivationToAsync(args)
                    .AsTask(cts.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                Log_FailedToActivate(_logger, redirectTimeout);
            }
            catch (Exception ex)
            {
                Log_ActivateError(_logger, ex);
            }
            finally
            {
                redirectSemaphore.Release();
            }
        });

        _ = PInvoke.CoWaitForMultipleObjects(
            (uint)CWMO_FLAGS.CWMO_DEFAULT,
            PInvoke.INFINITE,
            [new HANDLE(redirectSemaphore.SafeWaitHandle.DangerousGetHandle())],
            out _);
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        // If we already have a form, display the message now.
        // Otherwise, add it to the collection for displaying later.
        if (app?.AppWindow is MainWindow mainWindow)
        {
            // LOAD BEARING
            // This must be synchronous to ensure the method does not return
            // before the activation is fully handled and the parameters are processed.
            // The sending instance remains blocked until this returns; afterward it may quit,
            // causing the activation arguments to be lost.
            mainWindow.HandleLaunchNonUI(args);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to activate existing instance; timed out after {RedirectTimeout}.")]
    private static partial void Log_FailedToActivate(ILogger logger, TimeSpan redirectTimeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to activate existing instance")]
    private static partial void Log_ActivateError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't load winget")]
    private static partial void Log_FailureToLoadWinget(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting at {UtcNow}.")]
    private static partial void Log_StartingAt(ILogger logger, DateTime utcNow);
}
