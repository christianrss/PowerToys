// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.Core.ViewModels;

public interface IPageContext
{
    void ShowException(Exception ex, string? extensionHint = null);

    TaskScheduler Scheduler { get; }
}
