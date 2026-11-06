
- Do not hard-code taxonomies of what an estate contains (website/service/library kinds, layer ranks). Real estates have Windows services, desktop apps, jobs and shapes not anticipated. Derive labels from the project's own facts, keep the set open, order by dependencies only, and show "Not placed" for what cannot be placed instead of forcing it into a fixed bucket. (User correction, deployable-unit grouping.)
- Group the estate the way people recognise it (website, service, data source) rather than by technical layer; layers stay as detail inside a unit.

## No app-specific literals in the tool (behaviour probe)
- Correction: the behaviour probe hard-coded the sample's pages, field ids, credentials, customer ids and expected values. The tool must work on any legacy code base.
- Rule: nothing in `src/` may name an application's pages, fields, users, data or services. App-specific checks live in a data file (scenarios) supplied with the estate, never in code.
- Before claiming a part of the tool is generic, grep the whole of it for the sample's names; do not rely on having built it generically.
- Proof: planted fixture with different names and no web app or WCF host (must report what it could not exercise), plus a zero-hit sweep of `src/` for the sample's names.

## Run things only where a contract exists; read the rest from code
- Correction: the user questioned whether any generic crawl of a legacy app can work, since it all calls onward to databases, services and sign-in. They were right.
- Rule: derive behaviour from code (entry points, call paths, data, access) first. Run something only where a machine-readable contract exists (service descriptions) or where a person supplies the scenario. Do not propose a crawl or probe as the generic answer.
- Rule: a name match is not a resolution. Follow a call only when the calling code can reach the target; say why when it stops.

## Validate generated Word files against the schema
- Found: helper code produced run properties and table borders in the wrong order and tables without a column grid. The files opened in tools that tolerate it, but Word can reject or repair them.
- Rule: after changing any Word writer, validate every generated .docx with the OpenXML validator (0 issues) as well as comparing word/document.xml across runs.

## Name what the evidence shows, not what it suggests
- Correction: asked what external web service the sample uses, the honest answer was none. The tool had labelled code that only created an HttpClient as "External web services".
- Rule: a label for something outside the estate must reflect what was found (code that can make web calls, target unknown), never imply a service exists. Check each new label against a real case before it ships; when asked about a diagram item, read the code instead of trusting the diagram.
