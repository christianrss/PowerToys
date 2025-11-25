// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.Core.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.CmdPal.Core.ViewModels;

public partial class LogMessageViewModel : ExtensionObjectViewModel
{
    private readonly ExtensionObject<ILogMessage> _model;

    public string Message { get; private set; } = string.Empty;

    public LogMessageViewModel(ILogMessage message, IPageContext context, ILogger logger)
        : base(context, logger)
    {
        _model = new(message);
    }

    public override void InitializeProperties()
    {
        var model = _model.Unsafe;
        if (model is null)
        {
            return; // throw?
        }

        Message = model.Message;
    }
}
