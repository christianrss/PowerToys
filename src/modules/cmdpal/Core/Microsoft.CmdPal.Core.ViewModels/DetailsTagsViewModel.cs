// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.Core.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.CmdPal.Core.ViewModels;

public partial class DetailsTagsViewModel : DetailsElementViewModel
{
    public List<TagViewModel> Tags { get; private set; } = [];

    public bool HasTags => Tags.Count > 0;

    private readonly ExtensionObject<IDetailsTags> _dataModel;
    private readonly ILogger _logger;

    public DetailsTagsViewModel(IDetailsElement _detailsElement, WeakReference<IPageContext> context, ILogger logger)
        : base(_detailsElement, context, logger)
    {
        _logger = logger;
        _dataModel = new(_detailsElement.Data as IDetailsTags);
    }

    public override void InitializeProperties()
    {
        base.InitializeProperties();
        var model = _dataModel.Unsafe;
        if (model is null)
        {
            return;
        }

        Tags = model
            .Tags?
            .Select(t =>
        {
            var vm = new TagViewModel(t, PageContext, _logger);
            vm.InitializeProperties();
            return vm;
        })
            .ToList() ?? [];
        UpdateProperty(nameof(HasTags));
        UpdateProperty(nameof(Tags));
    }
}
