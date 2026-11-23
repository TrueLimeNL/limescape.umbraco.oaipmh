using Limescape.Umbraco.OaiPmh.Configuration;
using Limescape.Umbraco.OaiPmh.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.Extensions;

namespace Limescape.Umbraco.OaiPmh;

/// <summary>Registration of the OAI-PMH provider.</summary>
public static class OaiPmhServiceCollectionExtensions
{
    /// <summary>Name of the Umbraco pipeline filter that maps the endpoint.</summary>
    public const string PipelineFilterName = "Limescape.OaiPmh";

    /// <summary>
    /// Registers the provider in an Umbraco site: options from <see cref="OaiPmhOptions.SectionName"/> and a pipeline filter
    /// that maps the endpoint. Called by <see cref="Composers.OaiPmhComposer"/>; idempotent.
    /// </summary>
    public static IUmbracoBuilder AddOaiPmh(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOaiPmh(builder.Config);
        builder.AddNotificationHandler<global::Umbraco.Cms.Core.Notifications.ContentPublishedNotification, Records.RecordStateHandlers>()
            .AddNotificationHandler<global::Umbraco.Cms.Core.Notifications.ContentUnpublishedNotification, Records.RecordStateHandlers>()
            .AddNotificationHandler<global::Umbraco.Cms.Core.Notifications.ContentDeletedNotification, Records.RecordStateHandlers>()
            .AddNotificationHandler<global::Umbraco.Cms.Core.Notifications.ContentMovedToRecycleBinNotification, Records.RecordStateHandlers>()
            .AddNotificationHandler<global::Umbraco.Cms.Core.Notifications.ContentMovedNotification, Records.RecordStateHandlers>();
        if (!builder.Services.Any(d => d.ImplementationType == typeof(Records.ConsistencyJob)))
        {
            builder.Services.AddRecurringBackgroundJob<Records.ConsistencyJob>();
        }
        return builder;
    }

    /// <summary>Registers options, validation and the Umbraco pipeline filter. Idempotent.</summary>
    public static IServiceCollection AddOaiPmh(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(OaiPmhMarker)))
        {
            return services;
        }

        services.AddSingleton<OaiPmhMarker>();
        var section = configuration.GetSection(OaiPmhOptions.SectionName);
        services.AddOptions<OaiPmhOptions>()
            .Configure(options =>
            {
                // The binder appends to pre-initialized arrays. Configured scope/mapping must replace defaults.
                if (section.GetSection("DocumentTypes").Exists()) { options.DocumentTypes = []; }
                if (section.GetSection("DublinCoreMapping").Exists()) { options.DublinCoreMapping.Clear(); }
            })
            .Bind(configuration.GetSection(OaiPmhOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OaiPmhOptions>, OaiPmhOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<Records.IRecordStateStore, Records.RecordStateStore>();
        services.TryAddSingleton<Records.IContentSelector, Records.ContentSelector>();
        services.TryAddSingleton<Tokens.ResumptionTokenService>();
        services.TryAddSingleton<RateLimiting>();

        services.Configure<UmbracoPipelineOptions>(options => options.AddFilter(new UmbracoPipelineFilter(PipelineFilterName)
        {
            Endpoints = app => app.UseEndpoints(endpoints => endpoints.MapOaiPmh()),
        }));

        return services;
    }

    private sealed class OaiPmhMarker;
}
