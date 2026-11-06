# Flows: flows

Commit unknown.

- Flows are read from the code and followed by name, not run. A call is followed only when one target is plausible; otherwise the flow says why it stopped.
- Handlers are found from markup wiring and VB Handles clauses where present, and otherwise by naming convention, which each flow labels.
- A flow shows what the code can do, not how often or with which data.

## Website (Shop.Web)

### OrderScreen.CancelOrder

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 4)
- **Path:**
  1. `OrderScreen.CancelOrder` (entry point; src/Shop.Web/Orders.aspx.cs:18)
  2. `BasketService.Clear` (by name; src/Shop.Logic/Logic.cs:25)
  3. `BasketStore.Remove` (by name; src/Shop.Logic/Logic.cs:44)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Delete writes dbo.Baskets
- **Coverage:** 3 step(s) followed; 1 call(s) not followed because they go to the framework or to code outside the repository; 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.Orphan_Click

- **Starts as:** Page or control event (naming convention (ControlId_Event) in a page's code-behind; not confirmed by markup or a Handles clause)
- **Path:**
  1. `OrderScreen.Orphan_Click` (entry point; src/Shop.Web/Orders.aspx.cs:23)
- **Coverage:** 1 step(s) followed; 0 call(s) not followed because they go to the framework or to code outside the repository; 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.PlaceOrder_Click

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 3)
- **Reads from the request:** Form["quantity"]
- **Path:**
  1. `OrderScreen.PlaceOrder_Click` (entry point; src/Shop.Web/Orders.aspx.cs:11)
  2. `BasketService.Checkout` (by name; src/Shop.Logic/Logic.cs:17)
  3. `BasketStore.Save` (by name; src/Shop.Logic/Logic.cs:38)
  4. `BasketService.Walk` (same type; src/Shop.Logic/Logic.cs:30)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Insert writes dbo.Baskets
  - Outside system: `integration:ComInterop` - ComInterop: calls Activator.CreateInstance
  - Session: `state:Session` - Session("LastOrder") write
- **Could not follow:**
  - `payment.Pay` at src/Shop.Logic/Logic.cs:20: ambiguous: 2 methods could be the target (CardPayment.Pay, InvoicePayment.Pay); the tool does not guess
  - `Activator.CreateInstance` at src/Shop.Logic/Logic.cs:21: reflection (CreateInstance); the target is chosen at run time
  - `BasketService.Walk` at src/Shop.Logic/Logic.cs:30: recursive call back to a method already being followed
- **Coverage:** 4 step(s) followed; 2 call(s) not followed because they go to the framework or to code outside the repository; 3 point(s) where the tool stopped for a reason listed.
