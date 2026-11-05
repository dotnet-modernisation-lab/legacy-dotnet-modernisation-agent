using System.Xml.Linq;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

public static class PackagesConfigParser
{
    public static IReadOnlyList<PackageReferenceInfo> Parse(string packagesConfigPath, string source)
    {
        var document = XDocument.Load(packagesConfigPath);

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "package")
            .Select(element => new PackageReferenceInfo(
                (string?)element.Attribute("id") ?? "",
                (string?)element.Attribute("version") ?? "",
                (string?)element.Attribute("targetFramework"),
                source))
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
