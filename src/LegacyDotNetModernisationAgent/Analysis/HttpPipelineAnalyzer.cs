using LegacyDotNetModernisationAgent.Analysis.Facts;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Detects custom HTTP pipeline extensions: IHttpModule, IHttpHandler (and async/factory variants) and message handlers,
/// plus their registration in configuration. These are the Web Forms-era extension points; each needs its own migration
/// decision (middleware, endpoint handlers). Evidence is recorded as "Kind: detail".
/// </summary>
public sealed class HttpPipelineAnalyzer
{
    public const string HttpPipeline = "HttpPipeline";
    public const string Module = "HttpModule";
    public const string Handler = "HttpHandler";
    public const string MessageHandler = "MessageHandler";

    private static readonly string[] ModuleBases = ["IHttpModule"];
    private static readonly string[] HandlerBases = ["IHttpHandler", "IHttpAsyncHandler", "IHttpHandlerFactory", "HttpTaskAsyncHandler"];
    private static readonly string[] MessageHandlerBases = ["DelegatingHandler", "HttpMessageHandler"];

    public IReadOnlyList<LegacyTechnologyFinding> Analyze(
        IReadOnlyList<SourceFacts> sources,
        IReadOnlyList<WebConfigModel> configs)
    {
        var findings = new List<LegacyTechnologyFinding>();

        foreach (var source in sources)
        {
            foreach (var type in source.Types.Where(type => type.Kind == "Class"))
            {
                foreach (var baseType in type.Bases)
                {
                    var kind = ModuleBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase) ? Module
                        : HandlerBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase) ? Handler
                        : MessageHandlerBases.Contains(baseType.SimpleName, StringComparer.OrdinalIgnoreCase) ? MessageHandler
                        : null;

                    if (kind is not null)
                    {
                        findings.Add(new LegacyTechnologyFinding(HttpPipeline, $"{kind}: {type.Name} implements {baseType.SimpleName}", source.Path, baseType.Line, type.Name));
                    }
                }
            }
        }

        foreach (var config in configs)
        {
            foreach (var item in config.Auth.Where(item => item.Kind is "HttpModule" or "HttpHandler"))
            {
                findings.Add(new LegacyTechnologyFinding(HttpPipeline, $"{item.Kind} registration: {item.Detail}", config.Path, null, null));
            }
        }

        return findings;
    }
}
