# Components: legacy-webforms-migration

A component is a folder of code inside a part (the first folder below the project, or one level deeper when a project keeps everything in one folder). Dependencies are those found in the code; references between assemblies and links made only by naming convention are left out. "Not classified" means the tool could not place the code in a layer. Whether a dependency is wanted depends on the intended design, which the tool cannot see.

## Shared code

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| (project root) | not classified | UpgradeMarker | - | LegacyDataServiceHost: LegacyDataService/Services |

## LegacyDataServiceHost

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| LegacyDataService/BusinessObjects | BusinessObjects | CustomerCriteria, CustomerData | LegacyDataService/DataAccess | LegacyDataService/Services |
| LegacyDataService/DataAccess | DataAccess | LegacyCustomerDal | LegacyDataService/Models | LegacyDataService/BusinessObjects |
| LegacyDataService/Models | not classified | CustomerRecord | - | LegacyDataService/DataAccess, LegacyDataService/Services, LegacyWebFormsSample: BusinessObjects |
| LegacyDataService/Services | ServiceBoundary | CustomerNotFoundFault, CustomerRequest, ILegacyCustomerService, LegacyCustomerService | Shared code: (project root), LegacyDataService/BusinessObjects, LegacyDataService/Models | LegacyWebFormsSample: BusinessObjects |
| LegacyDataServiceHost/(project root) | Hosting | Global.asax | - | - |

## LegacyWebFormsSample

| Component | Layer | Main items | Depends on | Used by |
|---|---|---|---|---|
| (project root) | Hosting, Presentation | Global.asax, Site.Master | - | Pages |
| BusinessObjects | BusinessObjects, ServiceBoundary | CustomerCriteria, CustomerInfo, WcfClientFacade | LegacyDataServiceHost: LegacyDataService/Models, LegacyDataServiceHost: LegacyDataService/Services, Pages | Pages |
| Controls | Presentation | MigrationStatus.ascx | - | - |
| Pages | Presentation | AjaxToolkit.aspx, Compatibility.aspx, DataAccess.aspx, Default.aspx, Login.aspx, Profile.aspx | (project root), BusinessObjects | BusinessObjects |

## Loops between components

- 2 components depend on each other in a loop, directly or through the others: LegacyWebFormsSample: BusinessObjects, LegacyWebFormsSample: Pages.

## Presentation code that skips layers

No presentation code was found depending directly on data access or persistence.

## Most depended-on components

- LegacyDataServiceHost: LegacyDataService/Models is used by 3 other component(s): changing it affects LegacyDataService/DataAccess, LegacyDataService/Services, LegacyWebFormsSample: BusinessObjects.
- Shared code: (project root) is used by 1 other component(s): changing it affects LegacyDataServiceHost: LegacyDataService/Services.
- LegacyDataServiceHost: LegacyDataService/BusinessObjects is used by 1 other component(s): changing it affects LegacyDataService/Services.
- LegacyDataServiceHost: LegacyDataService/DataAccess is used by 1 other component(s): changing it affects LegacyDataService/BusinessObjects.
- LegacyDataServiceHost: LegacyDataService/Services is used by 1 other component(s): changing it affects LegacyWebFormsSample: BusinessObjects.
