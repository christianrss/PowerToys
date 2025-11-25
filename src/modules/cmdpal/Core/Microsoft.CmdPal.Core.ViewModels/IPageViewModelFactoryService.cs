// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;

namespace Microsoft.CmdPal.Core.ViewModels;

public interface IPageViewModelFactoryService
{
    /// <summary>
    /// Creates a new instance of the page view model for the given page type.
    /// </summary>
    /// <param name="page">The page for which to create the view model.</param>
    /// <param name="nested">Indicates whether the page is not the top-level page.</param>
    /// <param name="host">The command palette host that will host the page (for status messages)</param>
    /// <returns>A new instance of the page view model.</returns>
    PageViewModel? TryCreatePageViewModel(IPage page, bool nested, AppExtensionHost host);
}
