## Shared libraries and technologies

### LegacyQuotes.QuoteFor

- **Starts as:** Service operation (WebMethod on a WebService class (SOAP over ASMX))
- **Methods reached, in call order (indented by call depth):**
  1. `LegacyQuotes.QuoteFor` (entry point; src/Shop.Logic/Logic.cs:65)
    2. `PriceBook.Lookup` (by name; src/Shop.Logic/Logic.cs:60)
- **Coverage:** 2 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### PriceService.PriceOf

- **Starts as:** Service operation (OperationContract PriceOf on IPriceService, implemented by PriceService)
- **Methods reached, in call order (indented by call depth):**
  1. `PriceService.PriceOf` (entry point; src/Shop.Logic/Logic.cs:55)
    2. `PriceBook.Lookup` (by name; src/Shop.Logic/Logic.cs:60)
- **Coverage:** 2 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

## Website (Shop.Admin)

### AdminScreen.StoreChanges

- **Starts as:** Page or control event (Handles clause: SaveButton.Click)
- **Methods reached, in call order (indented by call depth):**
  1. `AdminScreen.StoreChanges` (entry point; src/Shop.Admin/Admin.aspx.vb:10)
    2. `BasketService.Clear` (by name; src/Shop.Logic/Logic.cs:30) **crosses into Shared libraries and technologies**
      3. `BasketStore.Remove` (by name; src/Shop.Logic/Logic.cs:77)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Delete writes dbo.Baskets
- **Coverage:** 3 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

## Website (Shop.Web)

### OrderScreen.CancelOrder

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 4)
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.CancelOrder` (entry point; src/Shop.Web/Orders.aspx.cs:18)
    2. `BasketService.Clear` (by name; src/Shop.Logic/Logic.cs:30) **crosses into Shared libraries and technologies**
      3. `BasketStore.Remove` (by name; src/Shop.Logic/Logic.cs:77)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Delete writes dbo.Baskets
- **Coverage:** 3 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.Orphan_Click

- **Starts as:** Page or control event (naming convention (ControlId_Event) in a page's code-behind; not confirmed by markup or a Handles clause)
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.Orphan_Click` (entry point; src/Shop.Web/Orders.aspx.cs:23)
- **Coverage:** 1 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.PlaceOrder_Click

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 3)
- **Reads from the request:** Form["quantity"]
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.PlaceOrder_Click` (entry point; src/Shop.Web/Orders.aspx.cs:11)
    2. `BasketService.Checkout` (by name; src/Shop.Logic/Logic.cs:21) **crosses into Shared libraries and technologies**
      3. `BasketStore.Save` (by name; src/Shop.Logic/Logic.cs:71)
      4. `BasketService.Walk` (same type; src/Shop.Logic/Logic.cs:35)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Insert writes dbo.Baskets
  - Outside system: `integration:ComInterop` - ComInterop: calls Activator.CreateInstance
  - Session: `state:Session` - Session("LastOrder") write
- **Could not follow:**
  - `payment.Pay` at src/Shop.Logic/Logic.cs:25: ambiguous: 2 methods could be the target (CardPayment.Pay, InvoicePayment.Pay); the tool does not guess
  - `Activator.CreateInstance` at src/Shop.Logic/Logic.cs:26: reflection (CreateInstance); the target is chosen at run time
  - `BasketService.Walk` at src/Shop.Logic/Logic.cs:35: recursive call back to a method already being followed
- **Coverage:** 4 step(s) followed; 3 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 3 point(s) where the tool stopped for a reason listed.
