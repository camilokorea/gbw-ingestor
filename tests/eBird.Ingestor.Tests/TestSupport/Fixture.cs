namespace eBird.Ingestor.Tests.TestSupport;

/// <summary>
/// Loads a recorded upstream response from <c>Fixtures/</c>. Recording the real payload shape —
/// extra fields, absent fields, mixed types and all — is the point: the DTOs are only ever
/// exercised against what eBird, iNaturalist and Xeno-Canto actually send.
/// </summary>
public static class Fixture
{
    public static string Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{fileName}' was not copied to the output directory.", path);
        }

        return File.ReadAllText(path);
    }
}
