using Flickr.Matcher.Application;
using Xunit;

namespace eBird.Ingestor.Tests.Flickr;

/// <summary>
/// The matcher decides which bird a photo shows, and gets it wrong silently in both directions:
/// a false positive hangs the wrong photo off a species page, a false negative leaves a species
/// with no photo at all. Neither throws, so this table is the only thing standing between a
/// taxonomy change and a wrong bird on the site.
///
/// Cases named <c>Documents…</c> assert behaviour that is currently wrong. They are here to make
/// the defect visible and to fail loudly when someone fixes it, not to bless it.
/// </summary>
public class SpeciesNameMatcherTests
{
    private const string Toucan = "Ramphastos sulfuratus";
    private const string Quetzal = "Pharomachrus mocinno";
    private const string ScarletMacaw = "Ara macao";

    [Theory]
    // ── Titles that should match ────────────────────────────────────────────────
    [InlineData("Ramphastos sulfuratus", null, true)]
    [InlineData("Keel-billed Toucan (Ramphastos sulfuratus) - Boca Tapada, Costa Rica", null, true)]
    [InlineData("RAMPHASTOS SULFURATUS in flight", null, true)]
    [InlineData("Ramphastos sulfuratus brevicarinatus", null, true)]
    [InlineData("ramphastos sulfuratus", null, true)]
    // ── Tags that should match ─────────────────────────────────────────────────
    [InlineData("Toucan", "costarica birding ramphastos sulfuratus toucan", true)]
    [InlineData("", "ramphastos sulfuratus", true)]
    // ── Should not match ───────────────────────────────────────────────────────
    [InlineData("Resplendent Quetzal (Pharomachrus mocinno)", "quetzal monteverde", false)]
    [InlineData("Toucan at sunrise", null, false)]
    [InlineData("Toucan at sunrise", "", false)]
    [InlineData("Ramphastos ambiguus", "ramphastos swainsonii", false)]
    // Genus and epithet split across lines — Flickr titles wrap, and the substring rule needs
    // them literally adjacent. A real, ordinary source of missed matches.
    [InlineData("Ramphastos\nsulfuratus", null, false)]
    // Flickr tags are frequently machine-generated with the spaces stripped out.
    [InlineData("Toucan", "costarica ramphastossulfuratus toucan", false)]
    public void Matches_TitleOrTags_AgainstAScientificName(string title, string? tags, bool expected)
    {
        Assert.Equal(expected, SpeciesNameMatcher.Matches(Toucan, title, tags));
    }

    [Fact]
    public void Matches_TreatsNullTitleAndTagsAsEmpty()
    {
        Assert.False(SpeciesNameMatcher.Matches(Toucan, null, null));
    }

    [Fact]
    public void Documents_GenusOnlyRow_MatchesAnyWordContainingIt()
    {
        // iNaturalist observations identified only to genus create a Species row whose
        // ScientificName is the bare genus ("Ara"). Substring matching then links that row to
        // every photo whose title happens to contain those letters — here a place name.
        var placeName = "Ara macao at Sarapiquí, Costa Rica";

        Assert.True(SpeciesNameMatcher.Matches("Ara", placeName, null));
        Assert.True(SpeciesNameMatcher.Matches("Ara", "Sarapiquí river cruise", null));
    }

    [Fact]
    public void Documents_EmptyScientificName_MatchesEveryPhoto()
    {
        // A Species row with a blank ScientificName links itself to every unlinked photo in the
        // database on the next run, because "".Contains anything is true.
        Assert.True(SpeciesNameMatcher.Matches(string.Empty, "Anything at all", null));
        Assert.True(SpeciesNameMatcher.Matches(string.Empty, string.Empty, string.Empty));
    }

    [Fact]
    public void MatchSpeciesIds_ReturnsEverySpeciesNamedInTheTitle()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[]
        {
            (1, Toucan),
            (2, Quetzal),
            (3, ScarletMacaw)
        }).ByScientificName;

        var matches = SpeciesNameMatcher.MatchSpeciesIds(
            "Ramphastos sulfuratus and Pharomachrus mocinno sharing a fruiting tree",
            tags: null,
            lookup);

        Assert.Equal(new[] { 1, 2 }, matches.OrderBy(id => id));
    }

    [Fact]
    public void MatchSpeciesIds_CombinesTitleAndTags()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[] { (1, Toucan), (2, Quetzal) }).ByScientificName;

        var matches = SpeciesNameMatcher.MatchSpeciesIds(
            "Ramphastos sulfuratus",
            "birding costarica pharomachrus mocinno",
            lookup);

        Assert.Equal(new[] { 1, 2 }, matches.OrderBy(id => id));
    }

    [Fact]
    public void MatchSpeciesIds_ReturnsNothingWhenNoNameAppears()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[] { (1, Toucan), (2, Quetzal) }).ByScientificName;

        Assert.Empty(SpeciesNameMatcher.MatchSpeciesIds("Sunset over Arenal", "landscape", lookup));
    }

    [Fact]
    public void MatchSpeciesIds_ReturnsEachSpeciesAtMostOnce()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[] { (1, Toucan) }).ByScientificName;

        var matches = SpeciesNameMatcher.MatchSpeciesIds(
            "Ramphastos sulfuratus, Ramphastos sulfuratus, Ramphastos sulfuratus",
            "ramphastos sulfuratus",
            lookup);

        Assert.Equal(new[] { 1 }, matches);
    }

    [Fact]
    public void BuildLookup_KeepsTheFirstIdForADuplicatedScientificName()
    {
        // eBird and iNat taxonomies overlap, so the same binomial can appear under two rows.
        var lookup = SpeciesNameMatcher.BuildLookup(new[]
        {
            (10, Toucan),
            (11, Toucan),
            (12, Quetzal)
        });

        Assert.Equal(10, lookup.ByScientificName[Toucan]);
        Assert.Equal(2, lookup.ByScientificName.Count);
        Assert.Equal(new[] { Toucan }, lookup.DuplicateScientificNames);
    }

    [Fact]
    public void BuildLookup_TreatsCasingDifferencesAsTheSameName()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[]
        {
            (10, "Ramphastos sulfuratus"),
            (11, "RAMPHASTOS SULFURATUS")
        });

        Assert.Single(lookup.ByScientificName);
        Assert.Equal(10, lookup.ByScientificName["ramphastos sulfuratus"]);
        Assert.Single(lookup.DuplicateScientificNames);
    }

    [Fact]
    public void BuildLookup_ReportsNoDuplicatesForACleanTable()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(new[] { (1, Toucan), (2, Quetzal), (3, ScarletMacaw) });

        Assert.Equal(3, lookup.ByScientificName.Count);
        Assert.Empty(lookup.DuplicateScientificNames);
    }

    [Fact]
    public void BuildLookup_HandlesAnEmptySpeciesTable()
    {
        var lookup = SpeciesNameMatcher.BuildLookup(Array.Empty<(int, string)>());

        Assert.Empty(lookup.ByScientificName);
        Assert.Empty(lookup.DuplicateScientificNames);
    }
}
