namespace EngagementPortal.Tests.Guard;

/// <summary>
/// Impedisce il ritorno della terminologia storica o del nome del fornitore
/// nel codice del prodotto. I pattern sono composti intenzionalmente per non
/// contenere essi stessi i termini che il test vieta.
/// </summary>
public class NamingGuardTests
{
    private static readonly string[] ForbiddenTerms =
    [
        string.Concat("b", "ip"),
        string.Concat("s", "taf"),
        string.Concat("s", "tid"),
        string.Concat("Ten", "antId"),
        string.Concat("tenant-", "provider")
    ];

    [Fact]
    public void ProductSources_DoNotContainLegacyTerms()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateProductFiles(root).ToList();

        Assert.NotEmpty(files);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            foreach (var term in ForbiddenTerms)
            {
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(root, file)} contiene '{term}'");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Terminologia non ammessa nel prodotto:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void WebSources_DoNotContainTenantConcept()
    {
        // Il termine storico non deve tornare nel prodotto. Il controllo è
        // limitato a EngagementPortal.Web: README e workflow possono citare
        // l'infrastruttura Azure (es. tenant-id dell'identity provider).
        var root = FindRepositoryRoot();
        var webRoot = Path.Combine(root, "EngagementPortal.Web");
        var term = string.Concat("ten", "ant");

        var violations = EnumerateProductFiles(root)
            .Where(file => file.StartsWith(
                webRoot, StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file)
                .Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Il concetto di tenant non deve comparire nel prodotto:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "EngagementPortal.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Root del repository non trovata.");
    }

    private static IEnumerable<string> EnumerateProductFiles(string root)
    {
        var webRoot = Path.Combine(root, "EngagementPortal.Web");

        if (Directory.Exists(webRoot))
        {
            foreach (var file in Directory.EnumerateFiles(
                webRoot,
                "*",
                SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file))
                    continue;

                var extension = Path.GetExtension(file);

                if (extension is ".cs" or ".html" or ".css"
                    or ".js" or ".json" or ".csproj")
                {
                    yield return file;
                }
            }
        }

        var workflows = Path.Combine(root, ".github", "workflows");

        if (Directory.Exists(workflows))
        {
            foreach (var file in Directory.EnumerateFiles(
                workflows,
                "*.yml",
                SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }

        var readme = Path.Combine(root, "README.md");

        if (File.Exists(readme))
            yield return readme;
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return parts.Contains("bin") || parts.Contains("obj");
    }
}
