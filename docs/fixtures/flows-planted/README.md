# Planted fixture for flow extraction

A small estate with names unrelated to any real application, in C# and VB.NET. It plants:
- a handler wired in markup under a non-conventional name (`CancelOrder`), and an `OnClientClick` that must be ignored;
- a handler found only by naming convention (`Orphan_Click`), which must be labelled unconfirmed;
- a VB handler found only by its `Handles` clause (`StoreChanges`);
- a call through an interface with two implementations (must be reported as ambiguous);
- a `List<string>.Add` call next to an unrelated estate class that also has an `Add` method (must not be followed);
- a reflection call, a recursive call, a request input, a session write and inline SQL;
- a service operation whose name is unrelated to any convention.

Run `flows docs/fixtures/flows-planted <dir>` and compare `flows.md` with `flows.output.md` from the first `##` heading onward (the header carries the repository name and commit, which change). `flows.output.md` was checked by hand against what the code plants.
