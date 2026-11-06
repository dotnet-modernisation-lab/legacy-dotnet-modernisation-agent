# Architecture: legacy-webforms-migration

Commit b1dcc50fa9dbfc95fe4a51fe5b96b20ae70d7e65. Drawn from the deterministic analysis; every box and arrow is something the tool found.

## Overview

![Architecture overview](architecture-overview.svg)

### How to read this

- **Cards** are the deployable parts of the estate. The coloured bar and badge show relative size, and the chips show confidence and open decisions. There is no overall size for the estate.
- **People and callers** (left) are deduced from what each project is. Who they are and where they connect from is not visible in a repository, so the diagram marks it as not known.
- **The dashed boundary** is the estate found in the repository. **Outside the estate** (right) holds the external systems the code reaches.
- **Arrows** point from a part to the part it depends on, labelled with the kind of link.

### Drill-down (Mermaid)

The same overview as a Mermaid diagram, then one diagram per part below.

```mermaid
flowchart LR
    u0(("<b>Other callers?</b><br/>Field-level rules<br/>Custom identity<br/>No authentication configured<br/>Shared-secret trust<br/><i>not visible in the repository</i>")):::person
    u0 -.-> a2
    u1(("<b>Browser users</b><br/>Field-level rules<br/>Forms sign-in<br/>Sign-in checked in code<br/>Shared-secret trust<br/><i>who and from where: not known</i>")):::person
    u1 --> a3
    subgraph Estate["The estate (found in the repository)"]
        a0["<b>Data source</b><br/>Data source<br/>3 code units, 0 technologies<br/>Size XS, Low confidence<br/>1 decision(s) waiting"]:::small
        a1["<b>Shared libraries and technologies</b><br/>Shared<br/>1 code units, 10 technologies<br/>Size M, Low confidence<br/>3 decision(s) waiting"]:::medium
        a2["<b>WCF service (LegacyDataServiceHost)</b><br/>WCF service<br/>10 code units, 2 technologies<br/>Size L, Low confidence<br/>5 decision(s) waiting"]:::large
        a3["<b>Website (LegacyWebFormsSample)</b><br/>Website<br/>12 code units, 6 technologies<br/>Size L, Low confidence<br/>6 decision(s) waiting"]:::large
        a4["<b>Not placed in any unit</b><br/>Not placed<br/>0 code units, 2 technologies<br/>Size XS, Low confidence<br/>1 decision(s) waiting"]:::notplaced
    end
    a2 -->|reads and writes data| a0
    a2 -->|uses shared code| a1
    a3 -->|uses shared code| a1
    a3 -->|WCF call| a2
    e0[("External web services")]:::external
    a3 -.-> e0
    e1[("File shares or local files")]:::external
    a2 -.-> e1
    classDef small fill:#d9f2e3,stroke:#2e7d4f,color:#1f2d3d
    classDef medium fill:#fff1c9,stroke:#b8860b,color:#1f2d3d
    classDef large fill:#fbd5d5,stroke:#b03030,color:#1f2d3d
    classDef notplaced fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d
    classDef person fill:#e6eef8,stroke:#3b6ea5,color:#1f2d3d
    classDef external fill:#ffffff,stroke:#8a96a6,stroke-dasharray: 5 5,color:#1f2d3d
```

## Data source

Red boxes wait on a decision. Dashed arrows go to other areas. Up to 25 components are drawn, those needing the most attention first.

```mermaid
flowchart TD
    subgraph L_Persistence["Persistence"]
        n0["<b>Customers.json</b><br/>Fixture"]
        n1["<b>Customers.json</b><br/>Fixture"]
        n2["<b>dbo.CustomerInfo_SelectById</b><br/>StoredProcedureContract<br/>decisions: D-DATA-CONTRACT"]:::gated
    end
    classDef gated fill:#fbd5d5,stroke:#b03030,color:#1f2d3d
    classDef other fill:#eef2f7,stroke:#44546a,color:#1f2d3d
    classDef tech fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d
```

## Shared libraries and technologies

Red boxes wait on a decision. Dashed arrows go to other areas. Up to 25 components are drawn, those needing the most attention first.

```mermaid
flowchart TD
    subgraph L_Unclassified["Unclassified"]
        n0["<b>UpgradeMarker</b><br/>Class"]
    end
    tech["<b>Technologies used</b><br/>1 Assembly<br/>2 AuthenticationMechanism<br/>2 ConfigurationSetting<br/>6 FrameworkAssembly<br/>2 LibraryType"]:::tech
    classDef gated fill:#fbd5d5,stroke:#b03030,color:#1f2d3d
    classDef other fill:#eef2f7,stroke:#44546a,color:#1f2d3d
    classDef tech fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d
```

## WCF service (LegacyDataServiceHost)

Red boxes wait on a decision. Dashed arrows go to other areas. Up to 25 components are drawn, those needing the most attention first.

```mermaid
flowchart TD
    subgraph L_BusinessObjects["BusinessObjects"]
        n1["<b>CustomerData</b><br/>CslaBusinessObject<br/>decisions: D-AUTH-TARGET, D-CSLA-TARGET, D-DATAPORTAL-TARGET"]:::gated
    end
    subgraph L_DataAccess["DataAccess"]
        n6["<b>LegacyCustomerDal</b><br/>Dal"]
    end
    subgraph L_Hosting["Hosting"]
        n8["<b>Global.asax</b><br/>GlobalAsax"]
    end
    subgraph L_ServiceBoundary["ServiceBoundary"]
        n5["<b>ILegacyCustomerService</b><br/>WcfContract<br/>decisions: D-WCF-TARGET"]:::gated
        n7["<b>LegacyCustomerService</b><br/>WcfService<br/>decisions: D-AUTH-TARGET, D-WCF-TARGET"]:::gated
        n9["<b>LegacyCustomerService.svc</b><br/>WcfHost<br/>decisions: D-WCF-TARGET"]:::gated
    end
    subgraph L_Unclassified["Unclassified"]
        n0["<b>CustomerCriteria</b><br/>Class"]
        n2["<b>CustomerNotFoundFault</b><br/>DataContract"]
        n3["<b>CustomerRecord</b><br/>DataContract"]
        n4["<b>CustomerRequest</b><br/>DataContract"]
    end
    n1 --> n0
    n1 --> n6
    n5 --> n2
    n5 --> n3
    n5 --> n4
    n6 --> n3
    n7 --> n1
    n7 --> n2
    n7 --> n3
    n7 --> n4
    n7 --> n5
    n9 --> n7
    x0(["Data source"]):::other
    n6 -.-> x0
    x1(["Shared libraries and technologies"]):::other
    n1 -.-> x1
    n7 -.-> x1
    tech["<b>Technologies used</b><br/>1 AuthenticationMechanism<br/>1 ConfigurationSetting<br/>1 Integration"]:::tech
    classDef gated fill:#fbd5d5,stroke:#b03030,color:#1f2d3d
    classDef other fill:#eef2f7,stroke:#44546a,color:#1f2d3d
    classDef tech fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d
```

## Website (LegacyWebFormsSample)

Red boxes wait on a decision. Dashed arrows go to other areas. Up to 25 components are drawn, those needing the most attention first.

```mermaid
flowchart TD
    subgraph L_BusinessObjects["BusinessObjects"]
        n1["<b>CustomerInfo</b><br/>CslaBusinessObject<br/>decisions: D-AUTH-TARGET, D-CSLA-TARGET, D-DATAPORTAL-TARGET"]:::gated
    end
    subgraph L_Hosting["Hosting"]
        n4["<b>Global.asax</b><br/>GlobalAsax"]
    end
    subgraph L_Presentation["Presentation"]
        n3["<b>MigrationStatus.ascx</b><br/>UserControl<br/>decisions: D-UI-TARGET"]:::gated
        n5["<b>Site.Master</b><br/>MasterPage<br/>decisions: D-UI-TARGET"]:::gated
        n6["<b>AjaxToolkit.aspx</b><br/>Page<br/>decisions: D-UI-TARGET"]:::gated
        n7["<b>Compatibility.aspx</b><br/>Page<br/>decisions: D-UI-TARGET"]:::gated
        n8["<b>DataAccess.aspx</b><br/>Page<br/>decisions: D-AUTH-TARGET, D-STATE-MANAGEMENT, D-UI-TARGET"]:::gated
        n9["<b>Default.aspx</b><br/>Page<br/>decisions: D-UI-TARGET"]:::gated
        n10["<b>Login.aspx</b><br/>Page<br/>decisions: D-AUTH-TARGET, D-STATE-MANAGEMENT, D-UI-TARGET"]:::gated
        n11["<b>Profile.aspx</b><br/>Page<br/>decisions: D-AUTH-TARGET, D-STATE-MANAGEMENT, D-UI-TARGET"]:::gated
    end
    subgraph L_ServiceBoundary["ServiceBoundary"]
        n2["<b>WcfClientFacade</b><br/>WcfClient<br/>decisions: D-AUTH-TARGET, D-WCF-TARGET"]:::gated
    end
    subgraph L_Unclassified["Unclassified"]
        n0["<b>CustomerCriteria</b><br/>Class"]
    end
    n1 --> n0
    n1 --> n10
    n1 --> n2
    n10 --> n5
    n11 --> n10
    n11 --> n5
    n2 --> n1
    n2 --> n10
    n6 --> n5
    n7 --> n5
    n8 --> n1
    n8 --> n10
    n8 --> n5
    n9 --> n5
    x0(["Shared libraries and technologies"]):::other
    n1 -.-> x0
    n2 -.-> x0
    x1(["WCF service (LegacyDataServiceHost)"]):::other
    n2 -.-> x1
    tech["<b>Technologies used</b><br/>2 AuthenticationMechanism<br/>1 ConfigurationSetting<br/>1 CrossCutting<br/>1 FrameworkAssembly<br/>1 Integration<br/>1 SessionState"]:::tech
    classDef gated fill:#fbd5d5,stroke:#b03030,color:#1f2d3d
    classDef other fill:#eef2f7,stroke:#44546a,color:#1f2d3d
    classDef tech fill:#ffffff,stroke:#888888,stroke-dasharray: 5 5,color:#1f2d3d
```
