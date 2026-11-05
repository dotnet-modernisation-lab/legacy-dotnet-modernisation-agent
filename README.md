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
| F. Strategy Agent, JSON analysis input, Word strategy | next |

## Use

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- assess <repositoryPath> [artifactsDirectory]
```

Writes, deterministically (byte-identical across runs):

- `Artifacts/legacy-project-assessment.json`: facts (projects, findings, binary evidence, WCF, data access, type structure)
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

## How the analysers work

- VB source is parsed with Roslyn's Visual Basic **syntax trees** (nothing is compiled or run), so comments and line continuations cannot cause false matches.
- Binaries (`Csla.dll`) are read with `System.Reflection.Metadata`: name, version, CLR metadata version, public key token, referenced assemblies and decoded public/protected method signatures for the CSLA types the source uses. The binary is never loaded.
- Every finding carries technology, evidence, file and line. Stored-procedure links say whether they are a `DirectReference` or only a `NamingConvention` match.
- Secret-looking `appSettings` values are redacted in all output.

## Known limits

- Only VB source is analysed for technologies. C# files are inventoried but not scanned (the sample has one trivial C# file).
- Type references are matched by name and resolved through project references; a name that stays ambiguous is skipped, never guessed.
- `Portable` is never assigned: without a compile and a behaviour check, code with no detected coupling is reported as `RequiresRecompile` / `PotentiallyPortable`.
- Edges with basis `NamingConvention` or `Convention` are weaker evidence than `Declared`, `DirectReference` or `SourceDeclaration`, and are labelled as such.
