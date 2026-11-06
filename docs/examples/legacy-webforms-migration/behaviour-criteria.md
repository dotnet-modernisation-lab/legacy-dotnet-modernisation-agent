# Behaviour criteria: legacy-webforms-migration

- These are candidates, derived from the code and configuration, and none is confirmed. They are written against the interface people and systems use, so a replacement can be checked against them without copying how the legacy system works.
- What a caller is shown (content, messages, status), how bad or missing input is handled, and what happens for a user who is not allowed cannot be read from code. They are listed under "to confirm" and need a person, a scenario file or a recorded session.
- Mechanisms the legacy system uses (sign-in technology, session state, the data portal, the service call between parts) are listed with each criterion as not required: a replacement may do these differently.
- A criterion with nothing declared and nothing found is listed in a short table, not as a full criterion; that is a statement about what the tool could see, not proof that nothing happens.

## Decide before carrying forward

These are things found in the current system that a replacement would copy by default. Each needs a decision: keep, change or drop.

1. **A shared application secret is used to vouch for calls between applications (reads credential-like setting LegacyTrustedApplicationName).** How callers prove who they are is a design decision for the replacement, as is where the secret is held. Copying this pattern would carry the weakness forward. _(authentication analysis)_
2. **2 components depend on each other in a loop, directly or through the others: LegacyWebFormsSample: BusinessObjects, LegacyWebFormsSample: Pages.** A replacement that reproduces this structure reproduces the coupling. Decide whether the dependency is intended behaviour or an accident of how the legacy code grew. _(LegacyWebFormsSample: BusinessObjects, LegacyWebFormsSample: Pages)_
3. **dbo.CustomerInfo_SelectById is run by the code, but its definition is not in the repository.** What it reads and writes cannot be checked against a replacement until the definition is obtained from the database. _(dbo.CustomerInfo_SelectById)_

## LegacyDataServiceHost

### BC-legacydataservicehost-operation-getcustomer-of-ilegacycustomerservice: Service operation GetCustomer of ILegacyCustomerService

_Status: candidate, derived from the code; not confirmed._

**Given** no access restriction is declared for it, so who may use it must be confirmed  
**And** these environment settings have values: LegacyCustomerFixturePath, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**When** a caller invokes the service operation GetCustomer of ILegacyCustomerService  
**Then** the system runs the database procedure dbo.CustomerInfo_SelectById (the tables inside it are in data-touch.md)  
**And** the system uses an outside system of kind file system  

**To confirm:**

- who may use this capability
- what is returned to the caller (content, messages, status)

**Used today, not required of a replacement:** custom principal; shared secret or trusted subsystem; the business-object framework's data portal

_Evidence: src/LegacyDataService/BusinessObjects/CustomerData.vb, src/LegacyDataService/DataAccess/LegacyCustomerDal.vb, src/LegacyDataService/Services/LegacyCustomerService.vb, src/LegacySharedAssembly/UpgradeMarker.cs_

## LegacyWebFormsSample

### BC-legacywebformssample-page-pages-compatibility-aspx-load: Request the page Pages/Compatibility.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** no access restriction is declared for it, so who may use it must be confirmed  
**When** a user requests the page Pages/Compatibility.aspx  
**Then** the system uses an outside system of kind external http  

**To confirm:**

- who may use this capability
- what is returned to the caller (content, messages, status)

_Evidence: src/LegacyWebFormsSample/Pages/Compatibility.aspx.vb_

### BC-legacywebformssample-page-pages-dataaccess-aspx-load: Request the page Pages/DataAccess.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** the user is signed in (declared: anonymous users are denied)  
**And** information remembered from an earlier request is available: LegacyUserRole  
**And** these environment settings have values: LegacyCustomerFixturePath, LegacyCustomerServiceUrl, LegacyTrustedApplicationName and LegacyTrustedApplicationSecret  
**And** the caller supplies the query parameter customerId  
**When** a user requests the page Pages/DataAccess.aspx with the query parameter customerId  
**Then** the system runs the database procedure dbo.CustomerInfo_SelectById (the tables inside it are in data-touch.md)  
**And** the system uses an outside system of kind file system  
**And** the system obtains part of its result from LegacyDataServiceHost  

**To confirm:**

- what a user who is not allowed is told or shown
- what happens when the query parameter customerId is missing or invalid
- what is returned to the caller (content, messages, status)

**Used today, not required of a replacement:** custom principal; forms authentication; identity checks in code; shared secret or trusted subsystem; session state to remember information between requests; the business-object framework's data portal; a call between parts of the estate

_Evidence: src/LegacyDataService/BusinessObjects/CustomerData.vb, src/LegacyDataService/DataAccess/LegacyCustomerDal.vb, src/LegacyDataService/Services/LegacyCustomerService.vb, src/LegacySharedAssembly/UpgradeMarker.cs and others_

### BC-legacywebformssample-page-pages-login-aspx-loginbutton-click: Use the LoginButton control (click) on the page Pages/Login.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** no access restriction is declared for it, so who may use it must be confirmed  
**And** the caller supplies the query parameter ReturnUrl  
**When** a user uses the LoginButton control (click) on the page Pages/Login.aspx with the query parameter ReturnUrl  
**Then** the system remembers for later requests: LastPage, LegacyUser and LegacyUserRole  

**To confirm:**

- who may use this capability
- what happens when the query parameter ReturnUrl is missing or invalid
- what is returned to the caller (content, messages, status)

**Used today, not required of a replacement:** forms authentication; session state to remember information between requests

_Evidence: src/LegacyWebFormsSample/Pages/Login.aspx.vb_

### BC-legacywebformssample-page-pages-profile-aspx-load: Request the page Pages/Profile.aspx

_Status: candidate, derived from the code; not confirmed._

**Given** the user is signed in (declared: anonymous users are denied)  
**And** information remembered from an earlier request is available: LastPage, LegacyUser and LegacyUserRole  
**When** a user requests the page Pages/Profile.aspx  
**Then** no effect on data, outside systems or remembered information was found, and what the caller receives is to be confirmed  

**To confirm:**

- what a user who is not allowed is told or shown
- what is returned to the caller (content, messages, status)

**Used today, not required of a replacement:** forms authentication; identity checks in code; session state to remember information between requests

_Evidence: src/LegacyWebFormsSample/Pages/Login.aspx.vb, src/LegacyWebFormsSample/Pages/Profile.aspx.vb, src/LegacyWebFormsSample/Web.config_

## Nothing declared or found

For these, no access rule, input or effect was found. That says what the tool could see, not that nothing happens.

| Capability | Part |
|---|---|
| Use the SubmitButton control (click) on the page Pages/AjaxToolkit.aspx | LegacyWebFormsSample |
| Request the page Pages/Default.aspx | LegacyWebFormsSample |
| Request the page Pages/Login.aspx | LegacyWebFormsSample |
