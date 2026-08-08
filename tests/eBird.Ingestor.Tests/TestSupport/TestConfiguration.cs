using Microsoft.Extensions.Configuration;

namespace eBird.Ingestor.Tests.TestSupport;

public static class TestConfiguration
{
    /// <summary>Builds an <see cref="IConfiguration"/> from literal key/value pairs.</summary>
    public static IConfiguration With(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    /// <summary>An empty configuration — used to assert on the missing-API-key guards.</summary>
    public static IConfiguration Empty() => With();
}
