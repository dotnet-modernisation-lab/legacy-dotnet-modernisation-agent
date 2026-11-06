# Planted fixture for structure and data-touch

Names are unrelated to any real application. Expected results, written before running:

- **Loop:** the folders `Alpha` and `Beta` call each other, so they form a loop; `Gamma` calls `Alpha` but is not in it.
- **Most depended-on:** `Alpha` is used by `Beta` and `Gamma`.
- **Layer skip:** website `Two` reaches the data access class directly.
- **Table written from two parts:** `dbo.Orders` is written by website `One` (through `GammaWorker`) and website `Two` (directly).
- **Table reached only through a procedure:** `dbo.AuditLog` is read by website `Two` inside `usp_Audit`, shown as read via procedure.
- **Procedure without a definition:** `usp_Missing` is executed, its tables are unknown, and the data-touch output says so.

Run `architecture docs/fixtures/structure-planted <dir>` and read `components.md` and `data-touch.md`.
