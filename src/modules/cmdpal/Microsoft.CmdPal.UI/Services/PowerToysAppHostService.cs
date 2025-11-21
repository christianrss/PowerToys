// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.Core.ViewModels;
using Microsoft.CmdPal.UI.ViewModels;

namespace Microsoft.CmdPal.UI.Services;

internal sealed class PowerToysAppHostService : IAppHostService
{
    public AppExtensionHost GetDefaultHost()
    {
        return CommandPaletteHost.Instance;
    }

    public AppExtensionHost GetHostForCommand(object? context, AppExtensionHost? currentHost)
    {
        AppExtensionHost? topLevelHost = null;
        if (context is TopLevelViewModel topLevelViewModel)
        {
            topLevelHost = topLevelViewModel.ExtensionHost;
        }

        return topLevelHost ?? currentHost ?? CommandPaletteHost.Instance;
    }
}
