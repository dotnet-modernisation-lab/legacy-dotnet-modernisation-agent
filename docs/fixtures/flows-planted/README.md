# Planted fixture for flow extraction

A small estate with names unrelated to any real application. It plants: a handler wired in markup under a non-conventional name (`CancelOrder`), a handler found only by naming convention (`Orphan_Click`), an `OnClientClick` that must be ignored, a call through an interface with two implementations (must be reported as ambiguous), a reflection call, a recursive call, a request input, a session write and inline SQL.

Run `flows docs/fixtures/flows-planted <dir>` and compare with `flows.output.md`, which was checked by hand against what the code plants.
