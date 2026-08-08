namespace ColorExtractor;

/// <summary>
/// Simple K-means clustering over RGB pixel data.
/// Optimised for clipart images with solid color regions.
/// </summary>
public static class KMeans
{
    private static readonly Random Rng = new(42); // deterministic seed

    /// <param name="seed">
    /// Optional per-call seed. The shared generator is seeded once per process, so a single
    /// <c>Cluster</c> call is only reproducible in isolation if it gets its own generator.
    /// Leave null to keep the tool's existing whole-run determinism; tests pass a seed.
    /// </param>
    public static List<(byte R, byte G, byte B, double Pct)> Cluster(
        List<(byte R, byte G, byte B)> pixels,
        int k,
        int maxIterations = 25,
        int? seed = null)
    {
        if (pixels.Count == 0) return [];

        var rng = seed is null ? Rng : new Random(seed.Value);

        // Initialize centroids with k-means++ style (spread out initial points)
        var centroids = InitializePlusPlus(pixels, k, rng);
        var assignments = new int[pixels.Count];

        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool changed = false;

            // Assign each pixel to the nearest centroid
            for (int i = 0; i < pixels.Count; i++)
            {
                int nearest = NearestCentroid(pixels[i], centroids);
                if (nearest != assignments[i])
                {
                    assignments[i] = nearest;
                    changed = true;
                }
            }

            if (!changed) break;

            // Recompute centroids as mean of assigned pixels
            var sums = new (long R, long G, long B, int Count)[k];
            for (int i = 0; i < pixels.Count; i++)
            {
                int c = assignments[i];
                sums[c].R += pixels[i].R;
                sums[c].G += pixels[i].G;
                sums[c].B += pixels[i].B;
                sums[c].Count++;
            }

            for (int c = 0; c < k; c++)
            {
                if (sums[c].Count == 0) continue;
                centroids[c] = (
                    (byte)(sums[c].R / sums[c].Count),
                    (byte)(sums[c].G / sums[c].Count),
                    (byte)(sums[c].B / sums[c].Count)
                );
            }
        }

        // Count pixels per cluster and compute coverage %
        var counts = new int[k];
        foreach (var a in assignments) counts[a]++;

        double total = pixels.Count;
        return Enumerable.Range(0, k)
            .Select(c => (centroids[c].R, centroids[c].G, centroids[c].B, counts[c] / total * 100.0))
            .ToList();
    }

    private static (byte R, byte G, byte B)[] InitializePlusPlus(
        List<(byte R, byte G, byte B)> pixels, int k, Random rng)
    {
        var centroids = new (byte R, byte G, byte B)[k];
        centroids[0] = pixels[rng.Next(pixels.Count)];

        for (int c = 1; c < k; c++)
        {
            // Choose next centroid with probability proportional to D² distance
            double[] distances = pixels
                .Select(p => (double)MinDistSq(p, centroids, c))
                .ToArray();
            double total = distances.Sum();
            double threshold = rng.NextDouble() * total;
            double cumulative = 0;
            int chosen = 0;
            for (int i = 0; i < distances.Length; i++)
            {
                cumulative += distances[i];
                if (cumulative >= threshold) { chosen = i; break; }
            }
            centroids[c] = pixels[chosen];
        }

        return centroids;
    }

    private static int MinDistSq(
        (byte R, byte G, byte B) pixel,
        (byte R, byte G, byte B)[] centroids,
        int count)
    {
        int minDist = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int d = DistSq(pixel, centroids[i]);
            if (d < minDist) minDist = d;
        }
        return minDist;
    }

    private static int NearestCentroid(
        (byte R, byte G, byte B) pixel,
        (byte R, byte G, byte B)[] centroids)
    {
        int nearest = 0;
        int minDist = int.MaxValue;
        for (int i = 0; i < centroids.Length; i++)
        {
            int d = DistSq(pixel, centroids[i]);
            if (d < minDist) { minDist = d; nearest = i; }
        }
        return nearest;
    }

    private static int DistSq(
        (byte R, byte G, byte B) a,
        (byte R, byte G, byte B) b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return dr * dr + dg * dg + db * db;
    }
}
