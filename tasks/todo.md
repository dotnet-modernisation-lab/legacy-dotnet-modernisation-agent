# Todo

Principle: the tool will be run against legacy code bases we have not seen. **Analysis must work on any .NET code base and say plainly what it could not understand.** Anything that needs a running environment, real traffic or sample-specific knowledge is deferred (below) and must not be assumed.

## Milestones (assessment prototype)
- [x] B. Repository inventory
- [x] C. Technology analysers (Web Forms, CSLA source, assembly, WCF, DAL/stored procedures)
- [x] D. Architectural graph, layer boundaries, impact, migration characteristics
- [x] E. Behaviour contracts, HTTP probe, verifiable baseline (proved on the sample)
- [ ] F. Strategy Agent, `LegacyMigrationAnalysisInput`, fact validation on return, JSON, Word strategy

## Now: analysis that works on any code base
- [ ] Discover solutions and projects without assuming a `.sln` (`.sln`, `.slnx`, or bare project files); flag projects not in any solution
- [ ] Survive unparseable projects/solutions: record a coverage gap, never crash
- [ ] Multi-targeting (`TargetFrameworks`) and SDK-style projects
- [ ] Coverage report in the assessment: what was analysed, what was skipped (unscanned languages, unsupported project types, missing binaries) and why it matters
- [ ] Known-but-unanalysed technologies (EF, NHibernate, Remoting, ASMX, MVC, Web API, Enterprise Library ...) reported as gaps, never silent
- [ ] Unassessed technology defaults to a human decision gate, never "portable"
- [ ] Regression: sample graph and migration analysis unchanged
- [ ] Run against code that is not the sample (this repo: C#, SDK-style, `.slnx`) and against a non-.NET folder (clean error)
- [ ] C# analyser (Web Forms/CSLA/WCF/DAL patterns in C# source)
- [ ] Authentication detection (Forms/Membership/Windows/OIDC) and a human decision gate for the authentication target
- [ ] Analysers as plug-ins declaring the facts they emit; policy rules as data

## Deferred (needs an environment, traffic or per-estate input)
- [ ] Probe scenarios as an input file (entry points, roles, steps, captures) instead of code; human approves each scenario file
- [ ] Pluggable authenticators for the probe (form login, Windows integrated, basic; pre-authenticated session for SSO/MFA; browser automation for complex flows); credentials from environment/secret store, never written to artifacts
- [ ] Pluggable hosting adapters (IIS Express, IIS, plain process); estates with several apps
- [ ] Evidence grade on every contract: Observed / Recorded / Derived / Documented / Claimed; only Observed and Recorded can prove behaviour preserved, Derived proves a contract unchanged
- [ ] Static behaviour extraction: access matrix (CSLA `AuthorizationRules`, `<authorization>`, role checks), validation rules and messages, WCF/data contracts, configuration contracts
- [ ] Ingest real traffic: IIS and WCF logs, APM/telemetry, HAR captures (usage and status codes; prioritises which behaviours matter)
- [ ] Database facts: stored-procedure definitions, schema, constraints, sample result sets
- [ ] Service metadata snapshots (WSDL/XSD) and old-versus-new contract compatibility diff (WSDL, procedure signatures, assembly API, config keys)
- [ ] In-process component characterisation (call business objects/DAL from a harness, golden-master outputs)
- [ ] Human sources as `Documented`/`Claimed` behaviours (specs, tickets, UAT scripts, SME input), confirmed before use
- [ ] Recorded-behaviour noise handling (normalise tokens, ViewState, cookies, timestamps)

## Known findings about the sample (carry into the strategy)
- Business objects and the WCF client facade depend on `HttpContext.Current` and call `LoginPage.GetCurrentRole()` (a page class); `WcfClientFacade` calls back into `CustomerInfo`
- Unknown customer and stopped WCF service show the same message ("DataPortal.Fetch failed")
- Stored-procedure contract is linked to the DAL by naming convention only; no code enforces it
- Two copies of `Customers.json`
