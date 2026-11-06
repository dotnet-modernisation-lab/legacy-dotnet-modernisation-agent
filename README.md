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
| E. Flow contracts (`flows`): what each entry point does, read from the code | first cut done (replaces the sample-specific probe) |
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
- `architecture <repositoryPath> [artifactsDirectory]` writes `architecture.docx`: the overview diagram (rendered to an image inside the document), the parts, who uses them and what they reach outside the estate, then for each part its entry points with what each one starts as, reads, and ends at (data, outside systems, session, configuration, authentication), the journeys that cross from one part to another, and what the tool could not follow or see. It opens with a contents list (linked, with outline levels for Word's navigation pane). The customer summary also gains a "Key journeys" section built from the flows that lead from one running part into another. It also writes `architecture-overview.svg` and `.png`. No model is used. The document text is identical on every run; the image depends on installed fonts. The Word files validate against the OpenXML schema.
- `diagram <repositoryPath> [artifactsDirectory]` draws the architecture from the deterministic facts (no model): `architecture-overview.svg` and `.mmd` show the deployable areas, their size, waiting decisions and what depends on what; `architecture.md` has the overview plus a detail view per area (Mermaid, renders on GitHub); one `architecture-detail-*.mmd` per area. Output is identical on every run.
- `Artifacts/legacy-dependency-graph.json`: nodes, typed edges with basis and evidence, layer boundaries, impact sets, page-to-stored-procedure paths
- `Artifacts/migration-analysis.json`: migration characteristics per component (rule ID, reason, evidence), human decision gates, potentially portable areas

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- flows <repositoryPath> [artifactsDirectory]
```

`flows` reads what each entry point of the application does, from the code, and writes `flows.json` and `flows.md`.
Entry points are found from evidence (markup event wiring, VB `Handles` clauses, service operations, global events, web
pipeline types, program entry, framework callbacks) and each flow says why it counts. Calls are followed by name through
the method index, only when one target is plausible; ambiguous calls, reflection, recursion and the depth limit are
listed as "could not follow" with the reason. Each flow ends at the data, outside systems, session keys, configuration and
authentication it touches, and lists the request inputs it reads and the declared access rules that mention it.
Nothing is built or run, and no app-specific names are in the tool. Design and limits: `docs/flow-contract-design.md`.
Not yet built: comparison against migrated code, the service-description check, and a scenario file interpreter
(the sample's former checks are kept as data in `docs/examples/legacy-webforms-migration/behaviour-scenarios.json`).

```text
dotnet run --project src/LegacyDotNetModernisationAgent -- wsdl <fileOrAddress> [artifactsDirectory]
dotnet run --project src/LegacyDotNetModernisationAgent -- wsdl-compare <old> <new> [artifactsDirectory]
```

`wsdl` reads a SOAP 1.1 service description (a file or an address, with the WSDL and schema files it imports) into
comparable facts: operations, message shapes (element names, types, how often each may appear, nil, allowed values),
faults, SOAP action and encoding, declared security policy names and endpoints. `wsdl-check <description> <repositoryPath>` checks a published description against the WCF contracts found in the code (contract names, operations, declared faults) and returns 1 when they disagree, so a description that belongs to an older build or another service is caught before it is used as a baseline. `wsdl-compare` compares two and
classifies each difference from the point of view of a caller built against the old contract: Breaking, Review,
Compatible or Informational. It returns exit code 1 when anything is Breaking, and 3 when nothing is but part of a description could not be read, so
"no differences" is never reported as clean when it is not. Anything it could not read (a missing import, WSDL 2.0,
`xs:any`, groups, SOAP encoding, header faults, WS-Addressing actions) is listed. It compares element namespaces, the order
of members in a sequence, SOAP headers and each binding. A description fetched from an address may only import further
addresses. To get the old description, save `...svc?singleWsdl` or `...asmx?WSDL` from the running legacy service, or run
`SvcUtil /t:metadata <service dll>` on the built assembly.
The rules are fixed and conservative; behaviour behind the service is not compared. Fetching an address is the only network
call. Planted example: `docs/fixtures/wsdl`.

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
