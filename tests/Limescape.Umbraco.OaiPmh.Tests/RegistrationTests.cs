using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace Limescape.Umbraco.OaiPmh.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        using var provider = Build([]);

        var options = provider.GetRequiredService<IOptions<OaiPmhOptions>>().Value;

        Assert.Equal("/oai", options.EndpointPath);
        Assert.Equal(OaiPmhGranularity.Seconds, options.Granularity);
        Assert.Null(options.BaseUrl);
    }

    [Fact]
    public void Options_bind_from_the_configuration_section()
    {
        using var provider = Build(new()
        {
            ["Limescape:OaiPmh:EndpointPath"] = "/harvest",
            ["Limescape:OaiPmh:RepositoryName"] = "Material bank",
            ["Limescape:OaiPmh:AdminEmail"] = "admin@example.org",
            ["Limescape:OaiPmh:BaseUrl"] = "https://example.org/harvest",
            ["Limescape:OaiPmh:Granularity"] = "Day",
        });

        var options = provider.GetRequiredService<IOptions<OaiPmhOptions>>().Value;

        Assert.Equal("/harvest", options.EndpointPath);
        Assert.Equal("Material bank", options.RepositoryName);
        Assert.Equal("admin@example.org", options.AdminEmail);
        Assert.Equal(new Uri("https://example.org/harvest"), options.BaseUrl);
        Assert.Equal(OaiPmhGranularity.Day, options.Granularity);
    }

    [Fact]
    public void Configured_types_and_mapping_replace_defaults_instead_of_extending_export_scope()
    {
        using var provider = Build(new()
        {
            ["Limescape:OaiPmh:DocumentTypes:0"] = "customMaterial",
            ["Limescape:OaiPmh:DublinCoreMapping:title:0"] = "customTitle"
        });
        var options = provider.GetRequiredService<IOptions<OaiPmhOptions>>().Value;
        Assert.Equal(["customMaterial"], options.DocumentTypes);
        Assert.Single(options.DublinCoreMapping);
        Assert.Equal(["customTitle"], options.DublinCoreMapping["title"]);
    }

    [Theory]
    [InlineData("Limescape:OaiPmh:EndpointPath", "oai")]
    [InlineData("Limescape:OaiPmh:EndpointPath", "/oai?verb=Identify")]
    [InlineData("Limescape:OaiPmh:BaseUrl", "/relative/oai")]
    [InlineData("Limescape:OaiPmh:BaseUrl", "ftp://example.org/oai")]
    [InlineData("Limescape:OaiPmh:ConsistencyJobTime", "1.00:00:00")]
    [InlineData("Limescape:OaiPmh:ConsistencyJobTime", "-01:00:00")]
    public void Invalid_settings_are_rejected(string key, string value)
    {
        using var provider = Build(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OaiPmhOptions>>().Value);
    }

    [Fact]
    public void Registers_one_umbraco_pipeline_filter_even_when_called_twice()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddOaiPmh(configuration);
        services.AddOaiPmh(configuration);
        using var provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredService<IOptions<UmbracoPipelineOptions>>().Value;

        var filter = Assert.IsType<UmbracoPipelineFilter>(
            Assert.Single(pipeline.PipelineFilters, f => f.Name == OaiPmhServiceCollectionExtensions.PipelineFilterName));
        Assert.NotNull(filter.Endpoints);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddOaiPmh(configuration).BuildServiceProvider();
    }
}
