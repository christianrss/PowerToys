// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AdaptiveCards.ObjectModel.WinUI3;
using AdaptiveCards.Templating;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CmdPal.Core.ViewModels;
using Microsoft.CmdPal.Core.ViewModels.Messages;
using Microsoft.CmdPal.Core.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.Extensions.Logging;
using Windows.Data.Json;

namespace Microsoft.CmdPal.UI.ViewModels;

public partial class ContentFormViewModel : ContentViewModel
{
    private readonly ExtensionObject<IFormContent> _formModel;
    private readonly ILogger _logger;

    public ContentFormViewModel(IFormContent _form, WeakReference<IPageContext> context, ILogger logger)
        : base(context, logger)
    {
        _formModel = new(_form);
        _logger = logger;
    }

    // Remember - "observable" properties from the model (via PropChanged)
    // cannot be marked [ObservableProperty]
    public string TemplateJson { get; protected set; } = "{}";

    public string StateJson { get; protected set; } = "{}";

    public string DataJson { get; protected set; } = "{}";

    public AdaptiveCardParseResult? Card { get; private set; }

    private static string Serialize(string? s) =>
        JsonSerializer.Serialize(s, JsonSerializationContext.Default.String);

    private static bool TryBuildCard(
        string templateJson,
        string dataJson,
        out AdaptiveCardParseResult? card,
        out Exception? error,
        ILogger logger)
    {
        card = null;
        error = null;

        try
        {
            var template = new AdaptiveCardTemplate(templateJson);
            var cardJson = template.Expand(dataJson);
            card = AdaptiveCard.FromJsonString(cardJson);
            return true;
        }
        catch (Exception ex)
        {
            Log_ErrorBuildindCard(logger, ex);
            error = ex;
            return false;
        }
    }

    public override void InitializeProperties()
    {
        var model = _formModel.Unsafe;
        if (model is null)
        {
            return;
        }

        TemplateJson = model.TemplateJson;
        StateJson = model.StateJson;
        DataJson = model.DataJson;

        if (TryBuildCard(TemplateJson, DataJson, out var builtCard, out var renderingError, _logger))
        {
            Card = builtCard;
            UpdateProperty(nameof(Card));
            return;
        }

        var errorPayload = $$"""
    {
        "error_message": {{Serialize(renderingError!.Message)}},
        "error_stack":   {{Serialize(renderingError.StackTrace)}},
        "inner_exception": {{Serialize(renderingError.InnerException?.Message)}},
        "template_json": {{Serialize(TemplateJson)}},
        "data_json":     {{Serialize(DataJson)}}
    }
    """;

        if (TryBuildCard(ErrorCardJson, errorPayload, out var errorCard, out var _, _logger))
        {
            Card = errorCard;
            UpdateProperty(nameof(Card));
            return;
        }

        UpdateProperty(nameof(Card));
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdaptiveOpenUrlAction))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdaptiveSubmitAction))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AdaptiveExecuteAction))]
    public void HandleSubmit(IAdaptiveActionElement action, JsonObject inputs)
    {
        if (action is AdaptiveOpenUrlAction openUrlAction)
        {
            WeakReferenceMessenger.Default.Send<LaunchUriMessage>(new(openUrlAction.Url));
            return;
        }

        if (action is AdaptiveSubmitAction or AdaptiveExecuteAction)
        {
            // Get the data and inputs
            var dataString = (action as AdaptiveSubmitAction)?.DataJson.Stringify() ?? string.Empty;
            var inputString = inputs.Stringify();

            _ = Task.Run(() =>
            {
                try
                {
                    var model = _formModel.Unsafe!;
                    if (model != null)
                    {
                        var result = model.SubmitForm(inputString, dataString);
                        WeakReferenceMessenger.Default.Send<HandleCommandResultMessage>(new(new(result)));
                    }
                }
                catch (Exception ex)
                {
                    ShowException(ex);
                }
            });
        }
    }

    private static readonly string ErrorCardJson = """
{
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "type": "AdaptiveCard",
    "version": "1.5",
    "body": [
        {
            "type": "TextBlock",
            "text": "Error parsing form from extension",
            "wrap": true,
            "style": "heading",
            "size": "ExtraLarge",
            "weight": "Bolder",
            "color": "Attention"
        },
        {
            "type": "TextBlock",
            "wrap": true,
            "text": "${error_message}",
            "color": "Attention"
        },
        {
            "type": "TextBlock",
            "text": "${error_stack}",
            "fontType": "Monospace"
        },
        {
            "type": "TextBlock",
            "wrap": true,
            "text": "Inner exception:"
        },
        {
            "type": "TextBlock",
            "wrap": true,
            "text": "${inner_exception}",
            "color": "Attention"
        }
    ]
}
""";

    [LoggerMessage(Level = LogLevel.Error, Message = "Error building adaptive card for form.")]
    static partial void Log_ErrorBuildindCard(ILogger logger, Exception ex);
}
