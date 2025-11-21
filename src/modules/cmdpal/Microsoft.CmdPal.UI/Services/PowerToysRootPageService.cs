// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.CmdPal.Core.Common.Services;
using Microsoft.CmdPal.Core.ViewModels;
using Microsoft.CmdPal.UI.ViewModels;
using Microsoft.CmdPal.UI.ViewModels.MainPage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.Extensions.Logging;
using WinRT;

namespace Microsoft.CmdPal.UI.Services;

internal sealed partial class PowerToysRootPageService : IRootPageService
{
    private readonly ILogger _logger;
    private readonly TopLevelCommandManager _topLevelCommandManager;
    private IExtensionWrapper? _activeExtension;
    private MainListPage _mainListPage;

    public PowerToysRootPageService(
        TopLevelCommandManager topLevelCommandManager,
        MainListPage mainListPage,
        ILogger logger)
    {
        _logger = logger;
        _topLevelCommandManager = topLevelCommandManager;
        _mainListPage = mainListPage;
    }

    public async Task PreLoadAsync()
    {
        await _topLevelCommandManager.LoadBuiltinsAsync();
    }

    public CommandPalette.Extensions.IPage GetRootPage()
    {
        return _mainListPage;
    }

    public async Task PostLoadRootPageAsync()
    {
        // After loading built-ins, and starting navigation, kick off a thread to load extensions.
        _topLevelCommandManager.LoadExtensionsCommand.Execute(null);

        await _topLevelCommandManager.LoadExtensionsCommand.ExecutionTask!;
        if (_topLevelCommandManager.LoadExtensionsCommand.ExecutionTask.Status != TaskStatus.RanToCompletion)
        {
            // TODO: Handle failure case
        }
    }

    private void OnPerformTopLevelCommand(object? context)
    {
        try
        {
            if (context is IListItem listItem)
            {
                _mainListPage.UpdateHistory(listItem);
            }
        }
        catch (Exception ex)
        {
            Log_ErrorUpdatingHistory(ex);
        }
    }

    public void OnPerformCommand(object? context, bool topLevel, AppExtensionHost? currentHost)
    {
        if (topLevel)
        {
            OnPerformTopLevelCommand(context);
        }

        if (currentHost is CommandPaletteHost host)
        {
            SetActiveExtension(host.Extension);
        }
        else
        {
            throw new InvalidOperationException("This must be a programming error - everything in Command Palette should have a CommandPaletteHost");
        }
    }

    public void SetActiveExtension(IExtensionWrapper? extension)
    {
        if (extension != _activeExtension)
        {
            // There's not really a CoDisallowSetForegroundWindow, so we don't
            // need to handle that
            _activeExtension = extension;

            var extensionWinRtObject = _activeExtension?.GetExtensionObject();
            if (extensionWinRtObject is not null)
            {
                try
                {
                    unsafe
                    {
                        var winrtObj = (IWinRTObject)extensionWinRtObject;
                        var intPtr = winrtObj.NativeObject.ThisPtr;
                        var hr = Native.CoAllowSetForegroundWindow(intPtr);
                        if (hr != 0)
                        {
                            Log_FailureToGiveForegroundRights(hr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log_ErrorSettingActiveExtension(ex);
                }
            }
        }
    }

    public void GoHome()
    {
        SetActiveExtension(null);
    }

    // You may ask yourself, why aren't we using CsWin32 for this?
    // The CsWin32 projected version includes some object marshalling, like so:
    //
    // HRESULT CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object pUnk,...)
    //
    // And if you do it like that, then the IForegroundTransfer interface isn't marshalled correctly
    internal sealed class Native
    {
        [DllImport("OLE32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows5.0")]
        internal static extern unsafe global::Windows.Win32.Foundation.HRESULT CoAllowSetForegroundWindow(nint pUnk, [Optional] void* lpvReserved);
    }

    [LoggerMessage(Level = LogLevel.Error)]
    partial void Log_ErrorSettingActiveExtension(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update history in PowerToysRootPageService")]
    partial void Log_ErrorUpdatingHistory(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error giving foreground rights: 0x{hr.Value:X8}")]
    partial void Log_FailureToGiveForegroundRights(global::Windows.Win32.Foundation.HRESULT hr);
}
