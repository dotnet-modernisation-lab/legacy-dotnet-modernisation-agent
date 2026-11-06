# Migration aspects: coverage matrix

Every aspect of a system that a migration has to consider, what this tool detects today, how strong that evidence is, and who decides. It is the checklist the Strategy Agent (Milestone F) reads from, so an aspect marked "not covered" is reported to the human as an unknown, never assumed safe.

Evidence strength:
- **Detected**: deterministic analysis of source, config or binaries.
- **Hint**: pattern observation for a human reviewer; never feeds policy.
- **Gap-listed**: the tool only says the technology is present and unanalysed (gate `D-UNASSESSED-TECH`).
- **None**: the tool says nothing. A human must supply it.

| # | Aspect | What matters in a migration | Coverage today | Evidence | Decided by |
|---|--------|-----------------------------|----------------|----------|------------|
| 1 | Project structure and dependencies | Solutions, projects, references, packages, target frameworks, build props | Discovery, references, packages, multi-targeting, MSBuild evaluation (props, conditions, central package versions); falls back to XML with a coverage gap | Detected | Tool |
| 2 | UI technology | Web Forms pages, controls, ViewState, master pages, user controls, third-party control suites | Pages, code-behind, designer merge, `System.Web` use. Third-party control suites only as unanalysed references | Detected / Gap-listed | Human (D-UI-TARGET) |
| 3 | Business-object framework | CSLA, other frameworks; DataPortal and remoting channel | CSLA source and assembly, DataPortal calls | Detected | Human (D-CSLA-TARGET, D-DATAPORTAL-TARGET) |
| 4 | Service boundaries | WCF contracts, services, clients, hosts, bindings; ASMX, Remoting | WCF in VB and C#. ASMX, Remoting reported as gaps | Detected / Gap-listed | Human (D-WCF-TARGET) |
| 5 | Data access | ADO.NET, DAL, stored procedures, EF/NHibernate, transactions | DAL contracts, stored-procedure names, inline SQL in code (tables read and written), `TransactionScope`/`BeginTransaction`. EF, NHibernate gap-listed | Detected (partial) | Human (D-DATA-CONTRACT) |
| 6 | Database | Schema, procedure bodies, triggers, jobs, constraints, volumes | `.sql` files parsed (procedures, views, triggers, tables, features such as dynamic SQL, cursors, linked servers, CLR); missing definitions reported. No live schema, data volumes or jobs | Detected (partial) | Human, with DBA (D-DATABASE-TARGET) |
| 7 | Data migration and cutover | Moving data, dual-running, rollback, downtime window | None | None | Human |
| 8 | State and session | InProc/StateServer/SQL session, ViewState, application and cache state | Session use, sessionState config | Detected | Human (D-STATE-MANAGEMENT) |
| 9 | Authentication and authorization | Forms, Windows, Membership, OWIN, claims, WCF security, role rules | Mechanisms, declared access rules, `UsesAuthentication` edges | Detected | Human (D-AUTH-TARGET) |
| 10 | HTTP pipeline | Modules, handlers, message handlers, `Global.asax` events | Modules, handlers, auth hooks | Detected | Human (D-HTTP-PIPELINE) |
| 11 | Configuration and secrets | appSettings, connection strings, machine-specific values, secret storage | Settings read in code, credential-like keys by name only | Detected / Hint | Human |
| 12 | Security posture | Weak crypto, cookie flags, transport security, rule order | Config hints only; no code-level review | Hint | Security reviewer |
| 13 | Logging, monitoring, health | log4net, NLog, EventLog, Trace, APM, health endpoints | Framework imports, Trace/EventLog calls, `GetLogger`; config not read | Detected (partial) | Human (D-OBSERVABILITY) |
| 14 | Error handling | Custom errors, global handlers, error pages | `Application_Error`, `Page_Error`, `GetLastError`, ELMAH; `customErrors` not read | Detected (partial) | Human (D-ERROR-HANDLING) |
| 15 | Background and scheduled work | Windows services, scheduled tasks, timers, threads, Hangfire/Quartz | Services, timers, threads, thread pool, Quartz/Hangfire (code only; Windows scheduled tasks live outside the repo) | Detected (partial) | Human (D-INTEGRATION-HOSTING) |
| 16 | Integrations | File shares, email/SMTP, FTP, MSMQ, COM interop, external HTTP services | All detected in code by name; config-side signals not read | Detected (partial) | Human (D-INTEGRATION-*) |
| 17 | Caching and performance | `HttpRuntime.Cache`, output caching, in-process state, performance baselines | ASP.NET cache, MemoryCache, Application state; output caching and performance baselines not covered | Detected (partial) | Human (D-CACHING) |
| 18 | Concurrency and threading | `HttpContext` use off request threads, static state, locks | `HttpContext.Current` use in logic layers only | Detected (partial) | Human |
| 19 | Localisation and time | Resources, cultures, time zones, date formats | Culture and resource use, culture `.resx` files, `DateTime.Now`, time-zone calls; config not read | Detected (partial) | Rule only (R-XCUT-LOCALISATION, R-XCUT-TIME) |
| 20 | Reporting and documents | Crystal Reports, SSRS, PDF/Excel generation | Report files and libraries detected; data sources and parameters not analysed | Detected (partial) | Human (D-REPORTING) |
| 21 | Third-party and binary dependencies | Unsupported libraries, licences, 32-bit or COM components, no source | Binary references, missing binaries, framework-assembly dependencies | Detected (partial) | Human |
| 22 | Build, CI and packaging | Build tooling, restore, signing, versioning | Builds are decided by MSBuild outside the tool | None (by design) | Human |
| 23 | Deployment and hosting | IIS settings, app pools, certificates, Windows-only assumptions, environments | IIS config sections in `web.config` only | Detected (partial) | Human |
| 24 | Behaviour baseline | What the system does now, so the new one can be checked | Flows read from the code (`flows`): entry points, call paths followed by name, data, session, configuration and declared access, with the points the tool could not follow. No run-time observation yet; a service-description check and a user-supplied scenario file are next | Derived | Human approves |
| 25 | Test coverage | Existing tests (usually none) | Deliberately not assumed | None (by design) | n/a |
| 26 | Compliance and audit | Audit trails, data protection, residency, retention | None | None | Human |
| 27 | Non-technical | Ownership, usage, release freedom, team skills, budget | None; traffic and log ingestion deferred | None | Human |

## Rules for using this matrix
- A row marked **None** is an unknown to report, not a pass. The strategy must list it as an open question.
- Moving a row from None to Detected means a new analyser that emits facts into the neutral model, with a policy rule or decision gate where it changes the migration.
- Third-party tools (AppCAT, Security Code Scan, Upgrade Assistant and its successor) can be optional evidence sources behind adapters. Their output is normalised into facts with the tool and version recorded. They do not replace the deterministic core.

## Suggested order for closing gaps
1. Row 1: done (MSBuild evaluation).
2. Rows 15, 16: done for code; config-side signals remain (see todo).
3. Rows 13, 14, 17: done for code (see todo for config gaps).
4. Rows 5, 6: done for `.sql` files and inline SQL; live schema and data remain deferred.
