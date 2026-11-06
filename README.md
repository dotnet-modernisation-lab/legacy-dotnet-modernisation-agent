# legacy-dotnet-modernisation-agent

Assessment-first modernisation tooling for legacy .NET Framework estates (VB.NET, ASP.NET Web Forms, CSLA, WCF).
Same pattern as `spring-modernisation-agent`: deterministic **facts**, deterministic **policy**, read-only AI **strategy**,
human review, and only then migration. No unit tests are assumed in the legacy estate; behaviour is recorded as contracts.

Reference sample: `legacy-webforms-migration`. Plan: `docs/assessment-prototype-plan.md` in that repo.

## Status

| Milestone | State |
|---|---|
| B. Repository inventory (solution, old-style projects, packages.config, references, framework/language) | done |
| C. Technology analysers (Web Forms, CSLA source, assembly, WCF, DAL/stored procedures) | done |
| D. Architectural graph, layer boundaries, impact, migration characteristics | done |
| E. Behaviour contracts and probe (`behave`, baseline, `--verify`) | done |
| Authentication and authorization detection, access rules, decision gate | done |
| F. Strategy: evidence bundle, validator, replay and deterministic strategy, Markdown and Word report | done (no model yet) |
| F. Model-backed Strategy Agent | next (needs a model decision) |

## Use

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- assess <repositoryPath> [artifactsDirectory]
```

Writes, deterministically (byte-identical across runs):

- `Artifacts/legacy-project-assessment.json`: facts (projects, findings, binary evidence, WCF, data access, type structure)
- `summary <repositoryPath> [artifactsDirectory]` writes the customer summary (`customer-summary.docx` and `.md`) in plain language: the parts of the estate with size and confidence, how they fit together and who uses them, the decisions only the customer's team can make (each as a question), the main risks, what the analysis could not see (as questions), and suggested next steps, plus `architecture-overview.svg`. No model is used; wording lives in `Strategy/ClientLanguage.cs` so it can be reviewed. It has no identifiers, prices, dates or overall size.
- `diagram <repositoryPath> [artifactsDirectory]` draws the architecture from the deterministic facts (no model): `architecture-overview.svg` and `.mmd` show the deployable areas, their size, waiting decisions and what depends on what; `architecture.md` has the overview plus a detail view per area (Mermaid, renders on GitHub); one `architecture-detail-*.mmd` per area. Output is identical on every run.
- `Artifacts/legacy-dependency-graph.json`: nodes, typed edges with basis and evidence, layer boundaries, impact sets, page-to-stored-procedure paths
- `Artifacts/migration-analysis.json`: migration characteristics per component (rule ID, reason, evidence), human decision gates, potentially portable areas

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- behave <repositoryPath> [artifactsDirectory] [--verify]
```

`behave` starts the built legacy app under IIS Express, drives it like a browser (sign-in, customers, postbacks, Session,
access control, and a stopped-WCF-host boundary test), and records the observations as named contracts in
`Artifacts/behaviour-baseline.json`. `--verify` re-runs the probe and reports any difference from the saved baseline.
The solution must already be built. The probe stops only the processes it started and never builds or edits the repository.
Nothing in the legacy solution changes and no test project is needed.

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- strategy <repositoryPath> [artifactsDirectory] [--model=deterministic|file:<strategy.json>]
```

Builds one bounded, sorted, ID-addressable evidence bundle (`legacy-migration-input.json`, with its SHA-256) from the facts, graph and
policy, asks a strategy model for the narrative, **validates it against the bundle**, and writes `legacy-migration-strategy.json`,
`legacy-strategy-validation.json`, `legacy-strategy-prompt.txt`, `migration-strategy.md` and `migration-strategy.docx`. Exit code 0 when accepted, 3 when
violations were found (the report is still written, with the violations).

- The strategy has no field for a classification, disposition or gate outcome: the model cannot change the policy, only describe and recommend.
- Every claim cites IDs from the bundle. A claim citing nothing is shown as Unverified; one citing an ID that is not in the bundle is Rejected (an invented fact).
- Violations reported, all of them, not just the first: `V-HASH`, `V-UNKNOWN-ID`, `V-GATE-MISSING`, `V-GATE-DUPLICATE`, `V-GATE-UNKNOWN`, `V-GATE-DECIDED`,
  `V-PHASE-GATE`, `V-PHASE-DUPLICATE`, `V-PHASE-UNASSESSED`, `V-QUESTION-MISSING`, `V-QUESTION-SHAPE`, `V-PROVENANCE`, `V-EMPTY`.
- Work areas are the deployable parts of the estate (each web application, service host, Windows service or other executable project, plus the data source and any shared libraries), found from project kinds, project references, file locations and graph edges, with an open set of labels. Each gets a relative T-shirt size (XS to XL) from a fixed, published rubric, a confidence (Low when unassessed or undecided gates make the size rest on unknowns) and a dependency-aware order. They are calculated by the tool, are in the evidence bundle, and are not added into one estate-wide size.
- Every aspect the tool cannot detect (`AspectCatalogue`, the machine-readable form of `docs/migration-aspects.md`) and every coverage gap must be an open question.
- `foundry` runs the Strategy Agent on Azure AI Foundry (read-only, no tools). Set `FOUNDRY_PROJECT_ENDPOINT`, `FOUNDRY_MODEL` and `FOUNDRY_AUTH_MODE` (`Development` = DefaultAzureCredential/`az login`, `AzureCli`, `ManagedIdentity`, optional `FOUNDRY_MANAGED_IDENTITY_CLIENT_ID`), the same variables as the Spring prototype. No keys are used. Only the evidence bundle is sent (names, paths, rule and gate IDs, coverage gaps; no source code and no secret values). The tool records the deployment and the bundle hash itself.
- `deterministic` (default) needs no model: it lists every gate and open question and orders phases by work area. `file:` replays a saved result, which is how
  the validator is proved: `scripts/plant_strategy_violations.py` plants one violation per variant and each must be rejected with its code.
- The `.docx` is a zip with timestamps, so its bytes differ between runs; compare `word/document.xml`, which is identical.

## How the analysers work

- VB.NET and C# source are parsed with Roslyn **syntax trees** (nothing is compiled or run, and no references are needed) into a language-neutral fact model; every analyser reads only that model.
- Binaries (`Csla.dll`) are read with `System.Reflection.Metadata`: name, version, CLR metadata version, public key token, referenced assemblies and decoded public/protected method signatures for the CSLA types the source uses. The binary is never loaded.
- Every finding carries technology, evidence, file and line. Stored-procedure links say whether they are a `DirectReference` or only a `NamingConvention` match.
- Secrets are never recorded: secret-looking `appSettings` values are redacted, credential-like settings are reported by key name only, and a `machineKey` or impersonation password is recorded only as present.

## Known limits

- VB.NET and C# source are analysed (syntax only, no compilation) through one language-neutral fact model, so each technology rule is written once. Other .NET languages (for example F#) are reported as coverage gaps.
- Type references are matched by name and resolved through project references; a name that stays ambiguous is skipped, never guessed.
- `Portable` is never assigned: without a compile and a behaviour check, code with no detected coupling is reported as `RequiresRecompile` / `PotentiallyPortable`.
- Edges with basis `NamingConvention` or `Convention` are weaker evidence than `Declared`, `DirectReference` or `SourceDeclaration`, and are labelled as such.
