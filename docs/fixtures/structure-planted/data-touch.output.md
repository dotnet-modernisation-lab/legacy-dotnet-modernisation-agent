## Written from more than one part or component

- dbo.Orders (table) is written by 2 parts (Fx.One, Fx.Two) through Shared code: (project root).

## Every data object

| Data object | Kind | Touched by | Written by | Read or executed by | Note |
|---|---|---|---|---|---|
| dbo.Orders | Table | Fx.One W; Fx.Two W | Shared code: (project root) | - |  |
| dbo.AuditLog | Table | Fx.Two R(p) | - | Shared code: (project root) |  |
| dbo.usp_Audit | Procedure | Fx.Two X | - | Shared code: (project root) |  |
| dbo.usp_Missing | Procedure | Fx.Two X | - | Shared code: (project root) | the procedure's definition is not in the repository, so the tables it touches are not known |
