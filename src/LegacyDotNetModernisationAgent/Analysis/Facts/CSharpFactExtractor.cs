namespace LegacyDotNetModernisationAgent.Analysis.Facts;

/// <summary>Temporary stub: replaced by the real extractor once the VB refactor is proven against the oracle.</summary>
public static class CSharpFactExtractor
{
    public static SourceFacts Extract(string path, string project, string text) =>
        new(path, project, "CSharp", [], [], [], [], [], [], []);
}
