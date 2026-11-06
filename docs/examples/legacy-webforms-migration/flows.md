# Flows: legacy-webforms-migration

Commit b1dcc50fa9dbfc95fe4a51fe5b96b20ae70d7e65.

- Flows are read from the code and followed by name, not run. A call is followed only when one target is plausible; otherwise the flow says why it stopped.
- Handlers are found from markup wiring and VB Handles clauses where present, and otherwise by naming convention, which each flow labels.
- A flow shows what the code can do, not how often or with which data.

## WCF service (LegacyDataServiceHost)

### CustomerData.DataPortal_Fetch

**Given** the business object declares that CustomerId, CustomerName and Status can be read only by role Admin  
**And** the configuration setting LegacyCustomerFixturePath is set  
**When** the framework calls CustomerData.DataPortal_Fetch, for example when business-object data is fetched through the data portal  
**Then** CustomerData.DataPortal_Fetch calls LegacyCustomerDal.GetCustomerById (matched by method name).  
**And** LegacyCustomerDal.GetCustomerById calls LegacyCustomerDal.ExecuteCustomerInfoSelectById.  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById calls LegacyCustomerDal.LoadCustomers.  
**And** LegacyCustomerDal.LoadCustomers calls LegacyCustomerDal.ResolveFixturePath.  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById runs dbo.CustomerInfo_SelectById (linked to this code by name only)  
**And** LegacyCustomerDal.LoadCustomers uses an outside system of kind file system  
**And** LegacyCustomerDal.ResolveFixturePath reads the configuration setting LegacyCustomerFixturePath and uses an outside system of kind file system  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 9 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Framework callback (naming convention of the business-object framework; called by the framework, not by application code)
- **Declared access that mentions it:** CslaAuthorizationRules: CustomerData.CustomerId AllowRead Admin; CslaAuthorizationRules: CustomerData.CustomerName AllowRead Admin; CslaAuthorizationRules: CustomerData.Status AllowRead Admin
- **Methods reached, in call order (indented by call depth):**
  1. `CustomerData.DataPortal_Fetch` (entry point; src/LegacyDataService/BusinessObjects/CustomerData.vb:45)
    2. `LegacyCustomerDal.GetCustomerById` (by name; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:19)
      3. `LegacyCustomerDal.ExecuteCustomerInfoSelectById` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:10)
        4. `LegacyCustomerDal.LoadCustomers` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:23)
          5. `LegacyCustomerDal.ResolveFixturePath` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:33)
- **Ends at:**
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
- **Coverage:** 5 step(s) followed; 9 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### LegacyCustomerService.GetCustomer

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**And** the code on this path uses custom principal and shared secret or trusted subsystem  
**And** the configuration settings LegacyCustomerFixturePath, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret are set  
**When** a caller invokes the service operation GetCustomer (OperationContract GetCustomer on ILegacyCustomerService, implemented by LegacyCustomerService)  
**Then** LegacyCustomerService.GetCustomer calls LegacyCustomerService.ValidateTrustedSubsystem, CustomerData.GetCustomer and UpgradeMarker.GetUpgradeStatus.  
**And** CustomerData.GetCustomer calls CustomerData.DataPortal_Fetch (called by the framework, not by name).  
**And** CustomerData.DataPortal_Fetch calls LegacyCustomerDal.GetCustomerById (matched by method name).  
**And** LegacyCustomerDal.GetCustomerById calls LegacyCustomerDal.ExecuteCustomerInfoSelectById.  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById calls LegacyCustomerDal.LoadCustomers.  
**And** LegacyCustomerDal.LoadCustomers calls LegacyCustomerDal.ResolveFixturePath.  
**And** LegacyCustomerService.ValidateTrustedSubsystem reads the configuration settings LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById runs dbo.CustomerInfo_SelectById (linked to this code by name only)  
**And** LegacyCustomerDal.LoadCustomers uses an outside system of kind file system  
**And** LegacyCustomerDal.ResolveFixturePath reads the configuration setting LegacyCustomerFixturePath and uses an outside system of kind file system  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 17 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Service operation (OperationContract GetCustomer on ILegacyCustomerService, implemented by LegacyCustomerService)
- **Methods reached, in call order (indented by call depth):**
  1. `LegacyCustomerService.GetCustomer` (entry point; src/LegacyDataService/Services/LegacyCustomerService.vb:13)
    2. `LegacyCustomerService.ValidateTrustedSubsystem` (same type; src/LegacyDataService/Services/LegacyCustomerService.vb:51)
    3. `CustomerData.GetCustomer` (named type; src/LegacyDataService/BusinessObjects/CustomerData.vb:12)
      4. `CustomerData.DataPortal_Fetch` (framework convention (business-object framework data portal); src/LegacyDataService/BusinessObjects/CustomerData.vb:45)
        5. `LegacyCustomerDal.GetCustomerById` (by name; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:19)
          6. `LegacyCustomerDal.ExecuteCustomerInfoSelectById` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:10)
            7. `LegacyCustomerDal.LoadCustomers` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:23)
              8. `LegacyCustomerDal.ResolveFixturePath` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:33)
    9. `UpgradeMarker.GetUpgradeStatus` (named type; src/LegacySharedAssembly/UpgradeMarker.cs:5) **crosses into Shared libraries and technologies**
- **Ends at:**
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: Thread.CurrentPrincipal
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericIdentity
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericPrincipal
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
- **Coverage:** 9 step(s) followed; 17 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

## Website (LegacyWebFormsSample)

### AjaxToolkitPage.SubmitButton_Click

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers AjaxToolkitPage.SubmitButton_Click (Handles clause: SubmitButton.Click)  
**Then** AjaxToolkitPage.SubmitButton_Click makes no call the tool could follow into the code it analysed  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 2 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: SubmitButton.Click)
- **Methods reached, in call order (indented by call depth):**
  1. `AjaxToolkitPage.SubmitButton_Click` (entry point; src/LegacyWebFormsSample/Pages/AjaxToolkit.aspx.vb:11)
- **Coverage:** 1 step(s) followed; 2 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### CompatibilityPage.Page_Load

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers CompatibilityPage.Page_Load (Handles clause: Me.Load)  
**Then** CompatibilityPage.Page_Load makes no call the tool could follow into the code it analysed  
**And** CompatibilityPage.Page_Load uses an outside system of kind external http  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 1 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `CompatibilityPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Compatibility.aspx.vb:7)
- **Ends at:**
  - Outside system: `integration:ExternalHttp` - ExternalHttp: creates HttpClient
- **Coverage:** 1 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### CustomerInfo.DataPortal_Fetch

**Given** the business object declares that CustomerId, CustomerName and Status can be read only by role Admin  
**And** the code on this path uses custom principal, forms authentication, identity checks in code and shared secret or trusted subsystem  
**And** the configuration settings LegacyCustomerFixturePath, LegacyCustomerServiceUrl, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret are set  
**When** the framework calls CustomerInfo.DataPortal_Fetch, for example when business-object data is fetched through the data portal  
**Then** CustomerInfo.DataPortal_Fetch calls CustomerInfo.IsAuthorizedUser and WcfClientFacade.GetCustomer.  
**And** CustomerInfo.IsAuthorizedUser calls CustomerInfo.IsAuthenticatedUser and LoginPage.GetCurrentRole.  
**And** WcfClientFacade.GetCustomer calls LegacyCustomerService.GetCustomer (implementation of ILegacyCustomerService). At LegacyCustomerService.GetCustomer the path leaves the LegacyWebFormsSample and continues in the LegacyDataServiceHost.  
**And** LegacyCustomerService.GetCustomer calls LegacyCustomerService.ValidateTrustedSubsystem, CustomerData.GetCustomer and UpgradeMarker.GetUpgradeStatus.  
**And** CustomerData.GetCustomer calls CustomerData.DataPortal_Fetch (called by the framework, not by name).  
**And** CustomerData.DataPortal_Fetch calls LegacyCustomerDal.GetCustomerById (matched by method name).  
**And** LegacyCustomerDal.GetCustomerById calls LegacyCustomerDal.ExecuteCustomerInfoSelectById.  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById calls LegacyCustomerDal.LoadCustomers.  
**And** 1 further steps are in the technical detail below  
**And** WcfClientFacade.GetCustomer reads the configuration settings LegacyCustomerServiceUrl, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** LegacyCustomerService.ValidateTrustedSubsystem reads the configuration settings LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById runs dbo.CustomerInfo_SelectById (linked to this code by name only)  
**And** LegacyCustomerDal.LoadCustomers uses an outside system of kind file system  
**And** LegacyCustomerDal.ResolveFixturePath reads the configuration setting LegacyCustomerFixturePath and uses an outside system of kind file system  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 31 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Framework callback (naming convention of the business-object framework; called by the framework, not by application code)
- **Declared access that mentions it:** CslaAuthorizationRules: CustomerInfo.CustomerId AllowRead Admin; CslaAuthorizationRules: CustomerInfo.CustomerName AllowRead Admin; CslaAuthorizationRules: CustomerInfo.Status AllowRead Admin
- **Methods reached, in call order (indented by call depth):**
  1. `CustomerInfo.DataPortal_Fetch` (entry point; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:86)
    2. `CustomerInfo.IsAuthorizedUser` (same type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:31)
      3. `CustomerInfo.IsAuthenticatedUser` (same type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:22)
      4. `LoginPage.GetCurrentRole` (named type; src/LegacyWebFormsSample/Pages/Login.aspx.vb:13)
    5. `WcfClientFacade.GetCustomer` (named type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:104)
      6. `LegacyCustomerService.GetCustomer` (implementation of ILegacyCustomerService; src/LegacyDataService/Services/LegacyCustomerService.vb:13) **crosses into WCF service (LegacyDataServiceHost)**
        7. `LegacyCustomerService.ValidateTrustedSubsystem` (same type; src/LegacyDataService/Services/LegacyCustomerService.vb:51)
        8. `CustomerData.GetCustomer` (named type; src/LegacyDataService/BusinessObjects/CustomerData.vb:12)
          9. `CustomerData.DataPortal_Fetch` (framework convention (business-object framework data portal); src/LegacyDataService/BusinessObjects/CustomerData.vb:45)
            10. `LegacyCustomerDal.GetCustomerById` (by name; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:19)
              11. `LegacyCustomerDal.ExecuteCustomerInfoSelectById` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:10)
                12. `LegacyCustomerDal.LoadCustomers` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:23)
                  13. `LegacyCustomerDal.ResolveFixturePath` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:33)
        14. `UpgradeMarker.GetUpgradeStatus` (named type; src/LegacySharedAssembly/UpgradeMarker.cs:5) **crosses into Shared libraries and technologies**
- **Ends at:**
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Identity.IsAuthenticated
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyCustomerServiceUrl` - appSettings[LegacyCustomerServiceUrl]
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: Thread.CurrentPrincipal
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericIdentity
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericPrincipal
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
- **Coverage:** 14 step(s) followed; 31 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### DataAccessPage.Page_Load

**Given** the configuration for Pages/DataAccess.aspx declares, in the order the tool read them, these rules: deny anonymous users; allow all users. ASP.NET applies the first rule that matches, so a user who is not signed in is denied before any later rule is reached  
**And** the code on this path uses custom principal, forms authentication, identity checks in code and shared secret or trusted subsystem  
**And** session state already holds "LegacyUserRole", which the code reads  
**And** the configuration settings LegacyCustomerFixturePath, LegacyCustomerServiceUrl, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret are set  
**And** the request supplies QueryString["customerId"]  
**When** the user triggers DataAccessPage.Page_Load (Handles clause: Me.Load)  
**Then** DataAccessPage.Page_Load calls LoginPage.GetCurrentRole and CustomerInfo.GetCustomer.  
**And** CustomerInfo.GetCustomer calls CustomerInfo.DataPortal_Fetch (called by the framework, not by name).  
**And** CustomerInfo.DataPortal_Fetch calls CustomerInfo.IsAuthorizedUser and WcfClientFacade.GetCustomer.  
**And** CustomerInfo.IsAuthorizedUser calls CustomerInfo.IsAuthenticatedUser.  
**And** WcfClientFacade.GetCustomer calls LegacyCustomerService.GetCustomer (implementation of ILegacyCustomerService). At LegacyCustomerService.GetCustomer the path leaves the LegacyWebFormsSample and continues in the LegacyDataServiceHost.  
**And** LegacyCustomerService.GetCustomer calls LegacyCustomerService.ValidateTrustedSubsystem, CustomerData.GetCustomer and UpgradeMarker.GetUpgradeStatus.  
**And** CustomerData.GetCustomer calls CustomerData.DataPortal_Fetch (called by the framework, not by name).  
**And** CustomerData.DataPortal_Fetch calls LegacyCustomerDal.GetCustomerById (matched by method name).  
**And** 3 further steps are in the technical detail below  
**And** DataAccessPage.Page_Load reads the session value "LegacyUserRole"  
**And** WcfClientFacade.GetCustomer reads the configuration settings LegacyCustomerServiceUrl, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** LegacyCustomerService.ValidateTrustedSubsystem reads the configuration settings LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** LegacyCustomerDal.ExecuteCustomerInfoSelectById runs dbo.CustomerInfo_SelectById (linked to this code by name only)  
**And** LegacyCustomerDal.LoadCustomers uses an outside system of kind file system  
**And** LegacyCustomerDal.ResolveFixturePath reads the configuration setting LegacyCustomerFixturePath and uses an outside system of kind file system  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 39 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Reads from the request:** QueryString["customerId"]
- **Declared access that mentions it:** WebConfig: location:Pages/DataAccess.aspx deny users=?; WebConfig: location:Pages/DataAccess.aspx allow users=*
- **Methods reached, in call order (indented by call depth):**
  1. `DataAccessPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/DataAccess.aspx.vb:11)
    2. `LoginPage.GetCurrentRole` (named type; src/LegacyWebFormsSample/Pages/Login.aspx.vb:13)
    3. `CustomerInfo.GetCustomer` (named type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:17)
      4. `CustomerInfo.DataPortal_Fetch` (framework convention (business-object framework data portal); src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:86)
        5. `CustomerInfo.IsAuthorizedUser` (same type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:31)
          6. `CustomerInfo.IsAuthenticatedUser` (same type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:22)
        7. `WcfClientFacade.GetCustomer` (named type; src/LegacyWebFormsSample/BusinessObjects/CustomerInfo.vb:104)
          8. `LegacyCustomerService.GetCustomer` (implementation of ILegacyCustomerService; src/LegacyDataService/Services/LegacyCustomerService.vb:13) **crosses into WCF service (LegacyDataServiceHost)**
            9. `LegacyCustomerService.ValidateTrustedSubsystem` (same type; src/LegacyDataService/Services/LegacyCustomerService.vb:51)
            10. `CustomerData.GetCustomer` (named type; src/LegacyDataService/BusinessObjects/CustomerData.vb:12)
              11. `CustomerData.DataPortal_Fetch` (framework convention (business-object framework data portal); src/LegacyDataService/BusinessObjects/CustomerData.vb:45)
                12. `LegacyCustomerDal.GetCustomerById` (by name; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:19)
                  13. `LegacyCustomerDal.ExecuteCustomerInfoSelectById` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:10)
                    14. `LegacyCustomerDal.LoadCustomers` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:23)
                      15. `LegacyCustomerDal.ResolveFixturePath` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:33)
            16. `UpgradeMarker.GetUpgradeStatus` (named type; src/LegacySharedAssembly/UpgradeMarker.cs:5) **crosses into Shared libraries and technologies**
- **Ends at:**
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
  - Session: `state:Session` - Session("LegacyUserRole") read
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Identity.IsAuthenticated
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyCustomerServiceUrl` - appSettings[LegacyCustomerServiceUrl]
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: Thread.CurrentPrincipal
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericIdentity
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericPrincipal
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
- **Coverage:** 16 step(s) followed; 39 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### DefaultPage.Page_Load

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**When** the user triggers DefaultPage.Page_Load (Handles clause: Me.Load)  
**Then** DefaultPage.Page_Load makes no call the tool could follow into the code it analysed  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `DefaultPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Default.aspx.vb:7)
- **Coverage:** 1 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### LoginPage.LoginButton_Click

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**And** the code on this path uses forms authentication  
**And** the request supplies QueryString["ReturnUrl"]  
**When** the user triggers LoginPage.LoginButton_Click (Handles clause: LoginButton.Click)  
**Then** LoginPage.LoginButton_Click makes no call the tool could follow into the code it analysed  
**And** LoginPage.LoginButton_Click writes the session values "LastPage", "LegacyUser" and "LegacyUserRole"  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 11 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: LoginButton.Click)
- **Reads from the request:** QueryString["ReturnUrl"]
- **Methods reached, in call order (indented by call depth):**
  1. `LoginPage.LoginButton_Click` (entry point; src/LegacyWebFormsSample/Pages/Login.aspx.vb:46)
- **Ends at:**
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.DefaultUrl
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Encrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookiePath
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: creates FormsAuthenticationTicket
  - Session: `state:Session` - Session("LastPage") write
  - Session: `state:Session` - Session("LegacyUser") write
  - Session: `state:Session` - Session("LegacyUserRole") write
- **Coverage:** 1 step(s) followed; 11 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### LoginPage.Page_Load

**Given** no declared access rule mentions this entry point, so the tool cannot say who may use it  
**And** the code on this path uses identity checks in code  
**When** the user triggers LoginPage.Page_Load (Handles clause: Me.Load)  
**Then** LoginPage.Page_Load makes no call the tool could follow into the code it analysed  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 1 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `LoginPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Login.aspx.vb:35)
- **Ends at:**
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
- **Coverage:** 1 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### ProfilePage.Page_Load

**Given** the configuration for Pages/Profile.aspx declares, in the order the tool read them, these rules: deny anonymous users; allow all users. ASP.NET applies the first rule that matches, so a user who is not signed in is denied before any later rule is reached  
**And** the code on this path uses forms authentication and identity checks in code  
**And** session state already holds "LastPage", "LegacyUser" and "LegacyUserRole", which the code reads  
**When** the user triggers ProfilePage.Page_Load (Handles clause: Me.Load)  
**Then** ProfilePage.Page_Load calls LoginPage.GetCurrentRole.  
**And** ProfilePage.Page_Load reads the session values "LastPage", "LegacyUser" and "LegacyUserRole"  

> This follows the order of calls as written in the source, not a run: conditions are not read, so a call listed here may not happen every time.  
> 12 further call(s) go to the framework, to code outside the repository, or to constructors, properties or lambdas the tool does not index.  

**Technical detail**

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Declared access that mentions it:** WebConfig: location:Pages/Profile.aspx deny users=?; WebConfig: location:Pages/Profile.aspx allow users=*
- **Methods reached, in call order (indented by call depth):**
  1. `ProfilePage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Profile.aspx.vb:7)
    2. `LoginPage.GetCurrentRole` (named type; src/LegacyWebFormsSample/Pages/Login.aspx.vb:13)
- **Ends at:**
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
  - Session: `state:Session` - Session("LastPage") read
  - Session: `state:Session` - Session("LegacyUser") read
  - Session: `state:Session` - Session("LegacyUserRole") read
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
- **Coverage:** 2 step(s) followed; 12 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.
