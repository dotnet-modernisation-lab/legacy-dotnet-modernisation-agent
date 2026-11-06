## Decide before carrying forward

These are things found in the current system that a replacement would copy by default. Each needs a decision: keep, change or drop.

1. **2 components depend on each other in a loop, directly or through the others: Fx.One: Fx.Logic/Alpha, Fx.One: Fx.Logic/Beta.** A replacement that reproduces this structure reproduces the coupling. Decide whether the dependency is intended behaviour or an accident of how the legacy code grew. _(Fx.One: Fx.Logic/Alpha, Fx.One: Fx.Logic/Beta)_
2. **Page.aspx (presentation code) depends directly on RepoDal (dataaccess) without going through a business or service layer.** A replacement that reproduces this structure reproduces the coupling. Decide whether the dependency is intended behaviour or an accident of how the legacy code grew. _(src/Fx.Two/Pages/Page.aspx.cs:8)_
3. **dbo.usp_Missing is run by the code, but its definition is not in the repository.** What it reads and writes cannot be checked against a replacement until the definition is obtained from the database. _(dbo.usp_Missing)_
4. **dbo.Orders is written by more than one part of the estate.** If the parts are migrated separately, they must agree on who owns this data and how changes are shared meanwhile. _(dbo.Orders)_

## Fx.One

### BC-fx-one-page-pages-page-aspx-start: Use a control on the page on the page Pages/Page.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** no access restriction is declared for it, so who may use it must be confirmed  
**When** a user uses a control on the page on the page Pages/Page.aspx  
**Then** the system writes to dbo.Orders  

**To confirm:**

- who may use this capability
- what is returned to the caller (content, messages, status)
- what happens beyond AlphaWorker.Run (recursive call back to a method already being followed)

_Evidence: src/Fx.Data/RepoDal.cs, src/Fx.Logic/Alpha/AlphaWorker.cs, src/Fx.Logic/Beta/BetaWorker.cs, src/Fx.Logic/Gamma/GammaWorker.cs and others_

## Fx.Two

### BC-fx-two-page-pages-page-aspx-start: Use a control on the page on the page Pages/Page.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** no access restriction is declared for it, so who may use it must be confirmed  
**When** a user uses a control on the page on the page Pages/Page.aspx  
**Then** the system runs the database procedure dbo.usp_audit (the tables inside it are in data-touch.md)  
**And** the system runs the database procedure dbo.usp_missing (the tables inside it are in data-touch.md)  
**And** the system writes to dbo.Orders  

**To confirm:**

- who may use this capability
- what is returned to the caller (content, messages, status)

_Evidence: src/Fx.Data/RepoDal.cs, src/Fx.Two/Pages/Page.aspx.cs_
