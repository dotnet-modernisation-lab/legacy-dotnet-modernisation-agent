# legacy-dotnet-modernisation-agent

Assessment-first modernisation tooling for legacy .NET Framework estates (VB.NET, ASP.NET Web Forms, CSLA, WCF).
Same pattern as `spring-modernisation-agent`: deterministic **facts**, deterministic **policy**, read-only AI **strategy**,
human review, and only then migration. No unit tests are assumed in the legacy estate; behaviour is recorded as contracts.

Reference sample: `legacy-webforms-migration`. Plan: `docs/assessment-prototype-plan.md` in that repo.

## Status

| Milestone | State |
|---|---|
| B. Repository inventory (solution, old-style projects, packages.config, references, framework/language) | done |
| C. Technology analysers (Web Forms, CSLA, assembly, WCF, DAL) | next |

## Use

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- assess <repositoryPath> [artifactsDirectory]
```

Writes `Artifacts/legacy-project-assessment.json`.
