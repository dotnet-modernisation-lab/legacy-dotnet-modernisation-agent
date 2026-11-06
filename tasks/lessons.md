
- Do not hard-code taxonomies of what an estate contains (website/service/library kinds, layer ranks). Real estates have Windows services, desktop apps, jobs and shapes not anticipated. Derive labels from the project's own facts, keep the set open, order by dependencies only, and show "Not placed" for what cannot be placed instead of forcing it into a fixed bucket. (User correction, deployable-unit grouping.)
- Group the estate the way people recognise it (website, service, data source) rather than by technical layer; layers stay as detail inside a unit.

## No app-specific literals in the tool (behaviour probe)
- Correction: the behaviour probe hard-coded the sample's pages, field ids, credentials, customer ids and expected values. The tool must work on any legacy code base.
- Rule: nothing in `src/` may name an application's pages, fields, users, data or services. App-specific checks live in a data file (scenarios) supplied with the estate, never in code.
- Before claiming a part of the tool is generic, grep the whole of it for the sample's names; do not rely on having built it generically.
- Proof: planted fixture with different names and no web app or WCF host (must report what it could not exercise), plus a zero-hit sweep of `src/` for the sample's names.
