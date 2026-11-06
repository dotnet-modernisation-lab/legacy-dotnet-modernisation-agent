## Breaking (5)

| Where | Old | New | Why |
|---|---|---|---|
| `binding/IOrderService/soap1.1-http/operation/GetOrder/soapAction` | http://example.test/orders/GetOrder | urn:orders:get | the SOAP action existing callers send no longer matches |
| `operation/IOrderService.Cancel` | exists | (absent) | the operation is gone; existing callers that use it will fail |
| `operation/IOrderService.GetOrder/in/GetOrder/region` | (absent) | xs:string; min=1; max=1; nillable=false | a new required input; existing callers do not send it |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/id` | xs:int; min=1; max=1; nillable=false | xs:long; min=1; max=1; nillable=false | the type changed from xs:int to xs:long |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/note` | xs:string; min=0; max=1; nillable=true | (absent) | an item that existing callers read is no longer returned |

## Review (2)

| Where | Old | New | Why |
|---|---|---|---|
| `binding/IOrderService/soap1.1-http/policy` | All\|ExactlyOne\|TransportBinding | (absent) | a declared security or messaging policy is gone |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/status/#enum` | Closed\|Open | Closed\|OnHold\|Open | callers may now receive values they were not built for (added OnHold) |

## Compatible (2)

| Where | Old | New | Why |
|---|---|---|---|
| `operation/IOrderService.GetOrder/in/GetOrder/locale` | (absent) | xs:string; min=0; max=1; nillable=false | a new optional input |
| `operation/IOrderService.Ship` | (absent) | exists | a new operation; existing callers do not use it |

## Informational (1)

| Where | Old | New | Why |
|---|---|---|---|
| `endpoint/OrderService/OrderPort` | http://old-host.example.test/OrderService.svc | https://new-host.example.test/orders | the service address differs; that is a deployment detail, not a contract change |


## What the tool could not read or compare

Security policies are compared only as the names of what they declare, not their settings. Behaviour behind the service is not compared.
