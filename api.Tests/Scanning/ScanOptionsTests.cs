using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

public sealed class ScanOptionsTests
{
    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddScanning();
        return builder.Build();
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Scan:CacheDirectory"] = @"C:\trivy-cache",
        ["Scan:WorkRoot"] = @"C:\scw",
    };

    [Fact]
    public async Task Minimal_configuration_starts_and_has_the_documented_defaults()
    {
        using var host = Build(Valid());

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<ScanOptions>>().Value;
        Assert.Equal("trivy", options.TrivyExecutablePath);
        Assert.Equal(15, options.ScanTimeoutMinutes);
        Assert.Equal(10, options.CloneTimeoutMinutes);
        Assert.Equal(5, options.DbUpdateTimeoutMinutes);
        Assert.Equal(7, options.MaxDbAgeDays);
        Assert.Equal(5, options.PollIntervalSeconds);
        Assert.Null(options.DbRepository);
        Assert.Null(options.ExpectedTrivyVersion);
        await host.StopAsync();
    }

    [Fact]
    public async Task Missing_cache_directory_stops_the_app_on_start()
    {
        var settings = Valid();
        settings.Remove("Scan:CacheDirectory");
        using var host = Build(settings);

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Scan:CacheDirectory", ex.Message);
    }

    [Fact]
    public async Task Missing_work_root_stops_the_app_on_start()
    {
        var settings = Valid();
        settings.Remove("Scan:WorkRoot");
        using var host = Build(settings);

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Scan:WorkRoot", ex.Message);
    }

    [Theory]
    [InlineData("Scan:ScanTimeoutMinutes", "0")]
    [InlineData("Scan:ScanTimeoutMinutes", "121")]
    [InlineData("Scan:MaxDbAgeDays", "0")]
    [InlineData("Scan:MaxDbAgeDays", "91")]
    [InlineData("Scan:CloneTimeoutMinutes", "0")]
    [InlineData("Scan:PollIntervalSeconds", "0")]
    public async Task Value_out_of_range_stops_the_app_on_start(string key, string value)
    {
        var settings = Valid();
        settings[key] = value;
        using var host = Build(settings);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
}
