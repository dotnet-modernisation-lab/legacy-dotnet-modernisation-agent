# Data touched: legacy-webforms-migration

- A part is the running part (website, service) whose entry points reach the code that touches the data; code in a shared library counts for every part that calls it. R means read, W written, X executed (a procedure). (p) means the touch happens inside a stored procedure the code runs, found by following the code to the procedure and then to the tables in its definition.
- Procedure definitions are read from SQL files in the repository. Where a procedure is linked to code only by its name, the link is a convention, not a call the tool saw. Tables touched by code the tool cannot read (dynamic SQL, definitions outside the repository, scheduled jobs) are not shown.

## Written from more than one part or component

No data object is written from more than one part or component.

## Every data object

| Data object | Kind | Touched by | Written by | Read or executed by | Note |
|---|---|---|---|---|---|
| dbo.CustomerInfo_SelectById | Procedure | LegacyDataServiceHost X; LegacyWebFormsSample X | - | LegacyDataServiceHost: LegacyDataService/DataAccess | the procedure's definition is not in the repository, so the tables it touches are not known |
