using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Limescape.Umbraco.OaiPmh.Composers;

/// <summary>Registers the OAI-PMH provider when the package is installed.</summary>
public sealed class OaiPmhComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddOaiPmh();
}
