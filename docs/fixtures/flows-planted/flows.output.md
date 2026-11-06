## Shared libraries and technologies

### LegacyQuotes.QuoteFor

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** a caller invokes the service operation QuoteFor (WebMethod attribute (SOAP over ASMX))  
**Then** LegacyQuotes.QuoteFor calls PriceBook.Lookup (matched by method name).  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 1 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Service operation (WebMethod attribute (SOAP over ASMX))
- **Methods reached, in call order (indented by call depth):**
  1. `LegacyQuotes.QuoteFor` (entry point; src/Shop.Logic/Logic.cs:65)
    2. `PriceBook.Lookup` (by name; src/Shop.Logic/Logic.cs:60)
- **Coverage:** 2 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### PriceService.PriceOf

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** a caller invokes the service operation PriceOf (OperationContract PriceOf on IPriceService, implemented by PriceService)  
**Then** PriceService.PriceOf calls PriceBook.Lookup (matched by method name).  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  

**Technical detail**

- **Starts as:** Service operation (OperationContract PriceOf on IPriceService, implemented by PriceService)
- **Methods reached, in call order (indented by call depth):**
  1. `PriceService.PriceOf` (entry point; src/Shop.Logic/Logic.cs:55)
    2. `PriceBook.Lookup` (by name; src/Shop.Logic/Logic.cs:60)
- **Coverage:** 2 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

## Website (Shop.Admin)

### AdminScreen.StoreChanges

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers AdminScreen.StoreChanges (Handles clause: SaveButton.Click)  
**Then** AdminScreen.StoreChanges calls BasketService.Clear (matched by method name).  
**And** BasketService.Clear calls BasketStore.Remove (matched by method name).  
**And** BasketStore.Remove writes the table dbo.Baskets  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 1 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

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

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers OrderScreen.CancelOrder (wired in markup src/Shop.Web/Orders.aspx line 4)  
**Then** OrderScreen.CancelOrder calls BasketService.Clear (matched by method name).  
**And** BasketService.Clear calls BasketStore.Remove (matched by method name).  
**And** BasketStore.Remove writes the table dbo.Baskets  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 1 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 4)
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.CancelOrder` (entry point; src/Shop.Web/Orders.aspx.cs:18)
    2. `BasketService.Clear` (by name; src/Shop.Logic/Logic.cs:30) **crosses into Shared libraries and technologies**
      3. `BasketStore.Remove` (by name; src/Shop.Logic/Logic.cs:77)
- **Ends at:**
  - Data: `db:table:dbo.baskets` - Delete writes dbo.Baskets
- **Coverage:** 3 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.Orphan_Click

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers OrderScreen.Orphan_Click (the tool found this handler by its name only; no markup or Handles clause confirms it is wired to a control)  
**Then** OrderScreen.Orphan_Click makes no call the tool could follow into the code it analysed  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  

**Technical detail**

- **Starts as:** Page or control event (naming convention (ControlId_Event) in a page's code-behind; not confirmed by markup or a Handles clause)
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.Orphan_Click` (entry point; src/Shop.Web/Orders.aspx.cs:23)
- **Coverage:** 1 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### OrderScreen.PlaceOrder_Click

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**And** the request supplies Form["quantity"]  
**When** the user triggers OrderScreen.PlaceOrder_Click (wired in markup src/Shop.Web/Orders.aspx line 3)  
**Then** OrderScreen.PlaceOrder_Click calls BasketService.Checkout (matched by method name).  
**And** BasketService.Checkout calls BasketStore.Save (matched by method name) and BasketService.Walk.  
**And** OrderScreen.PlaceOrder_Click writes the session value "LastOrder"  
**And** BasketService.Checkout uses an outside system of kind com interop  
**And** BasketStore.Save writes the table dbo.Baskets  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> The tool could not follow payment.Pay in BasketService.Checkout: ambiguous: 2 methods could be the target (CardPayment.Pay, InvoicePayment.Pay); the tool does not guess. What happens after it is not shown.  
> The tool could not follow Activator.CreateInstance in BasketService.Checkout: reflection (CreateInstance); the target is chosen at run time. What happens after it is not shown.  
> The tool could not follow BasketService.Walk in BasketService.Walk: recursive call back to a method already being followed. What happens after it is not shown.  
> 3 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (wired in markup src/Shop.Web/Orders.aspx line 3)
- **Reads from the request:** Form["quantity"]
- **Methods reached, in call order (indented by call depth):**
  1. `OrderScreen.PlaceOrder_Click` (entry point; src/Shop.Web/Orders.aspx.cs:11)
    2. `BasketService.Checkout` (by name; src/Shop.Logic/Logic.cs:21) **crosses into Shared libraries and technologies**
      3. `BasketStore.Save` (by name; src/Shop.Logic/Logic.cs:71)
      4. `BasketService.Walk` (same type; src/Shop.Logic/Logic.cs:35)
- **Ends at:**
  - Session: `state:Session` - Session("LastOrder") write
  - Outside system: `integration:ComInterop` - ComInterop: calls Activator.CreateInstance
  - Data: `db:table:dbo.baskets` - Insert writes dbo.Baskets
- **Could not follow:**
  - `payment.Pay` at src/Shop.Logic/Logic.cs:25: ambiguous: 2 methods could be the target (CardPayment.Pay, InvoicePayment.Pay); the tool does not guess
  - `Activator.CreateInstance` at src/Shop.Logic/Logic.cs:26: reflection (CreateInstance); the target is chosen at run time
  - `BasketService.Walk` at src/Shop.Logic/Logic.cs:35: recursive call back to a method already being followed
- **Coverage:** 4 step(s) followed; 3 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 3 point(s) where the tool stopped for a reason listed.
