# Planted fixture for service-contract comparison

`old/` is a SOAP 1.1 document/literal service split over a WSDL and an imported schema file (the way WCF publishes). `new/` is a single-file description of a changed version. Names are unrelated to any real application.

Differences planted, written down before running the tool:

| Planted change | Expected class |
|---|---|
| Operation `Cancel` removed | Breaking |
| `GetOrder` gains a required input `region` | Breaking |
| `GetOrder` result `id` changes from int to long | Breaking |
| `GetOrder` result `note` removed | Breaking |
| `GetOrder` SOAP action changed | Breaking |
| `status` gains the value `OnHold` in a result | Review |
| Transport security policy no longer declared | Review |
| `GetOrder` gains an optional input `locale` | Compatible |
| New operation `Ship` | Compatible |
| Service address changed | Informational |
| Complex type renamed `OrderDto` to `OrderV2`, same shape, and a named type made inline | no difference (type names are not on the wire) |

Run `wsdl-compare old/OrderService.wsdl new/OrderService.wsdl <dir>` and compare `service-contract-diff.md` with `expected-diff.md` from the first `##` heading onward. The command returns 1 because something is Breaking.
