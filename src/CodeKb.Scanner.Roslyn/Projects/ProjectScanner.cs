using System.Xml.Linq;

namespace CodeKb.Scanner.Roslyn.Projects;

public sealed record PackageReference(string Name, string? Version);

public sealed record ProjectInfo(
    string RelativePath,
    string Name,
    string? Sdk,
    IReadOnlyList<string> TargetFrameworks,
    string? RootNamespace,
    string? AssemblyName,
    string? LangVersion,
    bool NullableEnabled,
    bool ImplicitUsings,
    IReadOnlyList<PackageReference> PackageReferences,
    IReadOnlyList<string> ProjectReferences);

public sealed record SolutionInfo(
    string RelativePath,
    string Name,
    IReadOnlyList<string> Projects);

public interface IProjectScanner
{
    ProjectInfo? ParseProject(string relativePath, string xmlContent);
    SolutionInfo? ParseSolution(string relativePath, string content);
}

public sealed class ProjectScanner : IProjectScanner
{
    public ProjectInfo? ParseProject(string relativePath, string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent)) return null;

        XDocument doc;
        try { doc = XDocument.Parse(xmlContent); }
        catch { return null; }

        var root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Project", StringComparison.Ordinal))
            return null;

        var sdk = root.Attribute("Sdk")?.Value;
        var propertyGroups = root.Elements("PropertyGroup").ToList();

        string? Read(string name)
        {
            foreach (var pg in propertyGroups)
            {
                var v = pg.Element(name)?.Value;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        var tfm = Read("TargetFramework");
        var tfms = Read("TargetFrameworks");
        var frameworks = new List<string>();
        if (!string.IsNullOrEmpty(tfm)) frameworks.Add(tfm!);
        if (!string.IsNullOrEmpty(tfms))
            foreach (var f in tfms!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                frameworks.Add(f);

        var rootNs = Read("RootNamespace");
        var asmName = Read("AssemblyName");
        var lang = Read("LangVersion");
        var nullable = string.Equals(Read("Nullable"), "enable", StringComparison.OrdinalIgnoreCase);
        var implicitUsings = string.Equals(Read("ImplicitUsings"), "enable", StringComparison.OrdinalIgnoreCase);

        var packages = root.Descendants("PackageReference")
            .Select(e => new PackageReference(
                Name: e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? string.Empty,
                Version: e.Attribute("Version")?.Value ?? e.Element("Version")?.Value))
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToList();

        var projectRefs = root.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return new ProjectInfo(
            RelativePath: relativePath,
            Name: asmName ?? fileName,
            Sdk: sdk,
            TargetFrameworks: frameworks,
            RootNamespace: rootNs,
            AssemblyName: asmName,
            LangVersion: lang,
            NullableEnabled: nullable,
            ImplicitUsings: implicitUsings,
            PackageReferences: packages,
            ProjectReferences: projectRefs);
    }

    public SolutionInfo? ParseSolution(string relativePath, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var projects = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            // Project("{GUID}") = "Name", "relative\path.csproj", "{GUID}"
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal)) continue;
            var parts = trimmed.Split('"');
            if (parts.Length < 6) continue;
            var path = parts[5].Replace('\\', '/');
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                projects.Add(path);
        }
        return new SolutionInfo(
            RelativePath: relativePath,
            Name: Path.GetFileNameWithoutExtension(relativePath),
            Projects: projects);
    }
}
