# Flows: legacy-webforms-migration

Commit b1dcc50fa9dbfc95fe4a51fe5b96b20ae70d7e65.

- Flows are read from the code and followed by name, not run. A call is followed only when one target is plausible; otherwise the flow says why it stopped.
- Handlers are found from markup wiring and VB Handles clauses where present, and otherwise by naming convention, which each flow labels.
- A flow shows what the code can do, not how often or with which data.

## WCF service (LegacyDataServiceHost)

### CustomerData.DataPortal_Fetch

- **Starts as:** Framework callback (naming convention of the business-object framework; called by the framework, not by application code)
- **Declared access that mentions it:** CslaAuthorizationRules: CustomerData.CustomerId AllowRead Admin; CslaAuthorizationRules: CustomerData.CustomerName AllowRead Admin; CslaAuthorizationRules: CustomerData.Status AllowRead Admin
- **Methods reached, in call order (indented by call depth):**
  1. `CustomerData.DataPortal_Fetch` (entry point; src/LegacyDataService/BusinessObjects/CustomerData.vb:45)
    2. `LegacyCustomerDal.GetCustomerById` (by name; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:19)
      3. `LegacyCustomerDal.ExecuteCustomerInfoSelectById` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:10)
        4. `LegacyCustomerDal.LoadCustomers` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:23)
          5. `LegacyCustomerDal.ResolveFixturePath` (same type; src/LegacyDataService/DataAccess/LegacyCustomerDal.vb:33)
- **Ends at:**
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
- **Coverage:** 5 step(s) followed; 9 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### LegacyCustomerService.GetCustomer

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
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
- **Coverage:** 9 step(s) followed; 17 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

## Website (LegacyWebFormsSample)

### AjaxToolkitPage.SubmitButton_Click

- **Starts as:** Page or control event (Handles clause: SubmitButton.Click)
- **Methods reached, in call order (indented by call depth):**
  1. `AjaxToolkitPage.SubmitButton_Click` (entry point; src/LegacyWebFormsSample/Pages/AjaxToolkit.aspx.vb:11)
- **Coverage:** 1 step(s) followed; 2 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### CompatibilityPage.Page_Load

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `CompatibilityPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Compatibility.aspx.vb:7)
- **Ends at:**
  - Outside system: `integration:ExternalHttp` - ExternalHttp: creates HttpClient
- **Coverage:** 1 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### CustomerInfo.DataPortal_Fetch

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
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: Thread.CurrentPrincipal
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericIdentity
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericPrincipal
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Identity.IsAuthenticated
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Configuration: `config:LegacyCustomerServiceUrl` - appSettings[LegacyCustomerServiceUrl]
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
- **Coverage:** 14 step(s) followed; 31 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### DataAccessPage.Page_Load

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Reads from the request:** QueryString["customerId"]
- **Declared access that mentions it:** WebConfig: location:Pages/DataAccess.aspx allow users=*; WebConfig: location:Pages/DataAccess.aspx deny users=?
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
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: Thread.CurrentPrincipal
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericIdentity
  - Authentication: `auth:CustomPrincipal` - CustomPrincipal: creates GenericPrincipal
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Identity.IsAuthenticated
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationName
  - Authentication: `auth:SharedSecretOrTrustedSubsystem` - SharedSecretOrTrustedSubsystem: reads credential-like setting LegacyTrustedApplicationSecret
  - Configuration: `config:LegacyCustomerFixturePath` - appSettings[LegacyCustomerFixturePath]
  - Configuration: `config:LegacyCustomerServiceUrl` - appSettings[LegacyCustomerServiceUrl]
  - Configuration: `config:LegacyTrustedApplicationName` - appSettings[LegacyTrustedApplicationName]
  - Configuration: `config:LegacyTrustedApplicationSecret` - appSettings[LegacyTrustedApplicationSecret]
  - Data: `sp:dbo.CustomerInfo_SelectById` - LegacyCustomerDal.ExecuteCustomerInfoSelectById -> dbo.CustomerInfo_SelectById [NamingConvention]
  - Outside system: `integration:FileSystem` - FileSystem: calls File.Exists
  - Outside system: `integration:FileSystem` - FileSystem: calls File.OpenRead
  - Session: `state:Session` - Session("LegacyUserRole") read
- **Coverage:** 16 step(s) followed; 39 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### DefaultPage.Page_Load

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `DefaultPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Default.aspx.vb:7)
- **Coverage:** 1 step(s) followed; 0 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### LoginPage.LoginButton_Click

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

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Methods reached, in call order (indented by call depth):**
  1. `LoginPage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Login.aspx.vb:35)
- **Ends at:**
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
- **Coverage:** 1 step(s) followed; 1 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.

### ProfilePage.Page_Load

- **Starts as:** Page or control event (Handles clause: Me.Load)
- **Declared access that mentions it:** WebConfig: location:Pages/Profile.aspx allow users=*; WebConfig: location:Pages/Profile.aspx deny users=?
- **Methods reached, in call order (indented by call depth):**
  1. `ProfilePage.Page_Load` (entry point; src/LegacyWebFormsSample/Pages/Profile.aspx.vb:7)
    2. `LoginPage.GetCurrentRole` (named type; src/LegacyWebFormsSample/Pages/Login.aspx.vb:13)
- **Ends at:**
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.Decrypt
  - Authentication: `auth:FormsAuthentication` - FormsAuthentication: FormsAuthentication.FormsCookieName
  - Authentication: `auth:IdentityChecksInCode` - IdentityChecksInCode: Request.IsAuthenticated
  - Session: `state:Session` - Session("LastPage") read
  - Session: `state:Session` - Session("LegacyUser") read
  - Session: `state:Session` - Session("LegacyUserRole") read
- **Coverage:** 2 step(s) followed; 12 call(s) not followed because they go to the framework, to code outside the repository, or to members the tool does not index (constructors, properties, lambdas); 0 point(s) where the tool stopped for a reason listed.
