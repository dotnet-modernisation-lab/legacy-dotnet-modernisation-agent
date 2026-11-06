# Flow contracts: design

Status: first cut built (`flows` command). Replaces the sample-specific behaviour probe, which has been deleted (see `tasks/todo.md`, section H). Built: entry points, call following, terminals, gaps, markup and `Handles` wiring. Not built: comparison against migrated code, the service-description check, the scenario interpreter.

## 1. Why

A migration must preserve what the application *does*, not only what it is made of. The assessment already says what technology each part uses. It does not yet say, for example, "this button runs this logic, reads these inputs, touches this data and needs this role".

Running the application to find that out does not generalise: a real legacy app needs its own database, services, configuration and sign-in before any page does anything useful. So the primary source is the code itself, read statically. Running things is kept only where a machine-readable contract exists (section 9) or where a person supplies the scenario.

## 2. Terms

- **Flow**: one path of work from an entry point to the places where it ends (data, outside systems, session, response).
- **Entry point**: a method that something outside the code calls. The set of kinds is open and derived from facts (section 4); it is not a fixed list.
- **Flow contract**: the statement of what a flow must keep doing, written from the facts: its entry point, inputs, access rules, steps, data touched, side effects and the points where the tool could not follow.
- **Terminal**: where a flow stops being code we can follow: a stored procedure, table, file, email, external call, session key, configuration key, redirect or response.

## 3. What the facts already give, and what is missing

Available today (`SourceFacts`, syntax-only, VB.NET and C# alike):
- every type and method, with attributes, line range and whether it is public;
- every call with the callee name, its qualifier, string arguments and the enclosing type and method;
- object creations, member accesses and string literals, each with the enclosing type and method;
- graph nodes and typed edges (Invokes, DelegatesTo, ImplementsContract, ReadsData, WritesData, UsesSession, UsesIntegration and so on), and declared access rules.

Missing and needed:
1. **A method-level call index.** Today calls are folded into type-level edges. The facts keep the enclosing type and method, so the index can be built without new extraction.
2. **Event wiring.** Which handler a button or page event runs is declared in markup (`OnClick="..."`) or by `Handles` in VB. `MethodFact` does not carry `Handles`, and markup attributes are not read. Without this, handlers are found only by naming convention, which must be labelled as such.
3. **Variable types.** The facts are syntactic: `x.Save()` records the qualifier `x`, not its type. Resolution must therefore work by name, with an honest confidence (section 5).

## 4. Entry points

Found from evidence, each recording why it was treated as an entry point:

| Evidence | Entry point |
|---|---|
| Method in a page's code-behind named as a lifecycle or event handler, or wired in markup or by `Handles` | Page or control event |
| `OperationContract` method | Service operation |
| Type implementing a handler or module interface, or `Global.asax` event method | Web pipeline entry |
| `Main`, or a method started by a timer, thread or scheduler fact | Program or background entry |
| Method a framework invokes by convention (for example business-object data portal methods) | Framework callback |

Anything not matched is not an entry point; it can still appear inside a flow. A new technology adds a row here, never a code change elsewhere. A public method nobody calls is listed as "no caller found" rather than promoted to an entry point.

## 5. Following a call

From an entry point, calls are followed through the method index.

| Case | Treatment |
|---|---|
| Exactly one method has that name on a type reachable from the caller (same type, or a type the caller creates or declares) | Followed, basis `ResolvedByName` |
| Same type | Followed, basis `SameType` |
| Interface or base call with exactly one implementation (existing `ImplementsContract` / `Inherits` edges) | Followed to that implementation, basis `SingleImplementation` |
| More than one candidate | Not followed; recorded as ambiguous with the candidates listed |
| No candidate in the repository | Recorded as a call into code the tool cannot see, with the callee name |
| Reflection, dynamic invocation, late binding, delegates, events | Recorded as "could not follow", with the reason |

Rules:
- Every step carries its basis and location (file and line). Nothing is followed on a guess.
- Cycles stop at the first repeat and say so. A depth limit applies and is reported when reached.
- A step into a different project or deployable unit is marked, so the flow shows where it crosses a boundary.
- "Could not follow" reasons are open text derived from what was seen, not a fixed list.

## 6. What a flow records

```
Flow
  id                stable: derived from entry node and method, not from a counter
  entryPoint        node, method, kind, evidence (why it is an entry point)
  inputs            query string, form, route and method-parameter names read, config keys read
  access            declared rules that apply to the entry node (from existing access facts)
  steps[]           ordered method-level steps: node, method, basis, location, boundary crossed
  terminals[]       data read/written (procedure or table, with direction), integration (kind, name),
                    session keys read/written, configuration keys, redirects and other responses
  couldNotFollow[]  point, reason, location
  coverage          followed steps / total calls seen on the path (counts, no estate-wide totals)
```

Inputs and terminals are read from literals and call arguments already in the facts (for example the key in `Session("k")`, the procedure name in a command). A value computed at run time is recorded as "computed, not known".

## 7. Contracts and comparison

A flow contract is a checklist for the migrated system: an equivalent entry point must exist, accept the same inputs, apply equivalent access, and reach the same terminals (or a recorded, deliberate replacement). It does not require the code shape to match.

Comparison against migrated code reuses the same extractor on the target (it is syntax-level, so it reads modern .NET too) and compares terminals and access per flow. Pairing a legacy flow with its migrated counterpart is a person's or the strategy agent's decision, recorded in a mapping file; the tool never pairs by guesswork. Building the comparison is a later step. The first deliverable is the legacy-side flow set.

## 8. Outputs, determinism, wording

- `flows.json` (machine form) and `flows.md` (readable, one section per entry point grouped by deployable unit).
- Ordering is by unit, node id and method name; ids are stable; output is byte-identical across runs.
- No estate-wide totals. Counts appear only per flow.
- `flows` is the command. The `behave` command and its contracts named after the sample are gone; the service-description check (section 9) and the scenario interpreter are still to build. The sample's former checks are kept as data in `docs/examples/legacy-webforms-migration/behaviour-scenarios.json`.
- Customer summary gains plain-language flow lines later; internal ids stay out of it.

## 9. The parts that can be checked by running

- **Service description (WSDL or SOAP):** operations, parameters, data shapes and faults are a machine-readable contract. Compare it between old and new. Needs a reachable endpoint or a saved description file.
- **User-supplied scenario file:** steps, inputs and expected values written by someone who knows the app. Nothing app-specific lives in the tool. The sample's former checks move to `docs/examples/legacy-webforms-migration/behaviour-scenarios.json`.
- **Recorded real use (roadmap):** a browser or Fiddler capture of a person using the app, replayed against old and new. A person supplies data and sign-in.
- **Roadmap:** IIS logs to rank flows by real use; existing UI or test suites; stored-procedure characterisation against a test database.

## 10. Limits to state plainly

- Resolution is by name, so common method names (`Save`, `Load`) will be ambiguous in a large estate. They are reported as ambiguous, not guessed.
- Reflection, dependency injection containers, event wiring done at run time and dynamic data-portal dispatch end a flow with "could not follow".
- Event wiring is needed to find handlers reliably. Until markup and `Handles` are read, handlers are found by naming convention and labelled so.
- Constructors, property accessors and lambdas are not indexed, so calls into them are counted as not followed. Calls through a variable are followed only into a type the calling code refers to by name, or a service contract it opens a channel to.
- Overloads are treated as one method. Method parameters are not yet read as inputs. Timers and threads that start work are not yet entry points.
- A flow shows what the code can do, not how often or with which data. It is not a test result.

## 11. How it will be proved without unit tests

1. **Planted fixture with different names** (VB and C#), containing: a button handler calling through a business layer to a data layer and a procedure; an ambiguous call; an interface with two implementations; a reflection call; a recursive call; a service operation; a handler that is never called. The expected flow output is written by hand first, then compared with the tool's output.
2. **Sample estate:** review the flows against the code by hand for two or three entry points, including one that crosses the service boundary.
3. **Determinism:** two runs produce identical files.
4. **Generality sweep:** a search of `src/` for the sample's names returns nothing.
5. **Gaps:** a fixture with no web project and no service produces an output that says what it could not find, not an error.

## 12. Decisions needed

1. Should reading event wiring (markup attributes and `Handles`) be built first? It is the main accuracy gain for web pages.
2. Is the comparison against migrated code in scope now, or only the legacy-side flows?
3. Should the old probe be deleted outright, or kept for one release as sample-only? Recommendation: delete, with its checks moved to the scenario file.
