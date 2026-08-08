namespace Flickr.Matcher.Application;

/// <summary>
/// The scientific-name lookup the matcher runs a photo against, plus the scientific names that
/// appeared more than once in the species table and were therefore collapsed.
/// </summary>
public sealed record SpeciesLookup(
    IReadOnlyDictionary<string, int> ByScientificName,
    IReadOnlyList<string> DuplicateScientificNames);

/// <summary>
/// The species/photo matching rule used by the Flickr Matcher worker, isolated from EF Core so
/// it can be exercised without a database.
///
/// A photo matches a species when the species' scientific name occurs as a substring of the
/// photo title or of its space-delimited tag blob, case-insensitively. Substring — rather than
/// whole-word — matching is the worker's long-standing behaviour and is preserved verbatim here;
/// <c>SpeciesNameMatcherTests</c> pins down the near misses it accepts and rejects.
/// </summary>
public static class SpeciesNameMatcher
{
    /// <summary>
    /// True when <paramref name="scientificName"/> occurs in the title or the tags.
    /// Null title/tags are treated as empty, mirroring the worker's SQL projection.
    /// </summary>
    public static bool Matches(string scientificName, string? title, string? tags)
    {
        return (title ?? string.Empty).Contains(scientificName, StringComparison.OrdinalIgnoreCase)
            || (tags ?? string.Empty).Contains(scientificName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the ids of every species named in the photo's title or tags, at most once each.
    /// </summary>
    public static IReadOnlyList<int> MatchSpeciesIds(
        string? title,
        string? tags,
        IReadOnlyDictionary<string, int> speciesByScientificName)
    {
        var matches = new List<int>();
        var seen = new HashSet<int>();

        foreach (var (scientificName, speciesId) in speciesByScientificName)
        {
            if (Matches(scientificName, title, tags) && seen.Add(speciesId))
            {
                matches.Add(speciesId);
            }
        }

        return matches;
    }

    /// <summary>
    /// Collapses the species table into the lookup the matcher runs against. Scientific names are
    /// not unique in the database (eBird and iNat taxonomies overlap), so the first row for a name
    /// wins and the affected names are handed back for the worker to log.
    /// </summary>
    public static SpeciesLookup BuildLookup(IEnumerable<(int Id, string ScientificName)> species)
    {
        var groups = species
            .GroupBy(s => s.ScientificName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookup = groups.ToDictionary(
            g => g.Key,
            g => g.First().Id,
            StringComparer.OrdinalIgnoreCase);

        var duplicates = groups
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return new SpeciesLookup(lookup, duplicates);
    }
}
