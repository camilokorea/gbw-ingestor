using ColorExtractor;
using Xunit;

namespace eBird.Ingestor.Tests.Color;

/// <summary>
/// The colour extractor turns clustered pixels into the palette a species is filtered by on the
/// site, so a regression here is visible to users but throws nothing. Clustering is deterministic
/// given a seed, which makes it cheap to pin down exactly.
/// </summary>
public class KMeansTests
{
    private const int Seed = 42;

    private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);
    private static readonly (byte R, byte G, byte B) Blue = (0, 0, 255);

    [Fact]
    public void SeparatesTwoSolidColoursIntoTwoClusters()
    {
        var pixels = Repeat(Red, 50).Concat(Repeat(Blue, 50)).ToList();

        var clusters = KMeans.Cluster(pixels, k: 2, seed: Seed);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => (c.R, c.G, c.B) == Red);
        Assert.Contains(clusters, c => (c.R, c.G, c.B) == Blue);
        Assert.All(clusters, c => Assert.Equal(50.0, c.Pct, precision: 6));
    }

    [Fact]
    public void SplitsUnevenClustersInProportionToTheirPixelCount()
    {
        var pixels = Repeat(Red, 75).Concat(Repeat(Blue, 25)).ToList();

        var clusters = KMeans.Cluster(pixels, k: 2, seed: Seed);

        Assert.Equal(75.0, clusters.Single(c => (c.R, c.G, c.B) == Red).Pct, precision: 6);
        Assert.Equal(25.0, clusters.Single(c => (c.R, c.G, c.B) == Blue).Pct, precision: 6);
    }

    [Fact]
    public void ProducesTheSameResultTwiceForTheSameSeed()
    {
        var pixels = Jittered().ToList();

        var first = KMeans.Cluster(pixels, k: 3, seed: Seed);
        var second = KMeans.Cluster(pixels, k: 3, seed: Seed);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CentresEachClusterOnTheMeanOfItsPixels()
    {
        // Two noisy blobs around red and blue: the centroids should land on the blob means,
        // not on whichever pixel happened to seed them.
        var clusters = KMeans.Cluster(Jittered().ToList(), k: 2, seed: Seed);

        var reddish = clusters.Single(c => c.R > c.B);
        var bluish = clusters.Single(c => c.B > c.R);

        Assert.InRange(reddish.R, 245, 255);
        Assert.InRange(reddish.G, 0, 10);
        Assert.InRange(reddish.B, 0, 10);

        Assert.InRange(bluish.B, 245, 255);
        Assert.InRange(bluish.R, 0, 10);
        Assert.InRange(bluish.G, 0, 10);
    }

    [Fact]
    public void ReturnsTheSingleColourForAFlatImage()
    {
        var clusters = KMeans.Cluster(Repeat((17, 34, 51), 100).ToList(), k: 1, seed: Seed);

        var only = Assert.Single(clusters);
        Assert.Equal(((byte)17, (byte)34, (byte)51), (only.R, only.G, only.B));
        Assert.Equal(100.0, only.Pct, precision: 6);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyImage()
    {
        Assert.Empty(KMeans.Cluster([], k: 5, seed: Seed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void CoveragePercentagesAlwaysSumToOneHundred(int k)
    {
        // Every pixel must be accounted for, including when k exceeds the number of distinct
        // colours and some clusters come back empty.
        var clusters = KMeans.Cluster(Jittered().ToList(), k, seed: Seed);

        Assert.Equal(k, clusters.Count);
        Assert.Equal(100.0, clusters.Sum(c => c.Pct), precision: 6);
    }

    private static IEnumerable<(byte R, byte G, byte B)> Repeat((byte R, byte G, byte B) colour, int count) =>
        Enumerable.Repeat(colour, count);

    /// <summary>Two blobs of 50 pixels scattered a few units around pure red and pure blue.</summary>
    private static IEnumerable<(byte R, byte G, byte B)> Jittered()
    {
        for (var i = 0; i < 50; i++)
        {
            yield return ((byte)(250 + i % 6), (byte)(i % 5), (byte)(i % 5));
        }

        for (var i = 0; i < 50; i++)
        {
            yield return ((byte)(i % 5), (byte)(i % 5), (byte)(250 + i % 6));
        }
    }
}
