// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CmdPal.Core.Common.Services.Telemetry;
using Microsoft.CmdPal.Core.ViewModels.Messages;
using Microsoft.CmdPal.UI.Events;
using Microsoft.PowerToys.Telemetry;

namespace Microsoft.CmdPal.UI.Services.Telemetry;

internal class TelemetryService :
    ITelemetryService,
    IRecipient<BeginInvokeMessage>,
    IRecipient<CmdPalInvokeResultMessage>
{
    public TelemetryService()
    {
        WeakReferenceMessenger.Default.Register<BeginInvokeMessage>(this);
        WeakReferenceMessenger.Default.Register<CmdPalInvokeResultMessage>(this);
    }

    public void WriteEvent(TelemetryEventBase telemetryEvent) => PowerToysTelemetry.Log.WriteEvent(telemetryEvent);

    public void Receive(CmdPalInvokeResultMessage message)
    {
        PowerToysTelemetry.Log.WriteEvent(new InvokeResultEvent(message.Kind));
    }

    public void Receive(BeginInvokeMessage message)
    {
        PowerToysTelemetry.Log.WriteEvent(new BeginInvokeEvent());
    }

    public void LogRunQuery(string query, int resultCount, ulong durationMs)
    {
        PowerToysTelemetry.Log.WriteEvent(new RunQueryEvent(query, resultCount, durationMs));
    }

    public void LogRunCommand(string command, bool asAdmin, bool success)
    {
        PowerToysTelemetry.Log.WriteEvent(new RunCommandEvent(command, asAdmin, success));
    }

    public void LogOpenUri(string uri, bool isWeb, bool success)
    {
        PowerToysTelemetry.Log.WriteEvent(new OpenUriEvent(uri, isWeb, success));
    }
}
