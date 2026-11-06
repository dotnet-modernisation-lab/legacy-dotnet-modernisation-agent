## Breaking (15)

| Where | Old | New | Why |
|---|---|---|---|
| `binding/IOrderService/soap1.1-http[OrderBinding]/operation/GetOrder/soapAction` | http://example.test/orders/GetOrder | urn:orders:get | the SOAP action existing callers send no longer matches |
| `binding/IOrderService/soap1.1-http[OrderSecureBinding]/operation/GetOrder/soapAction` | http://example.test/orders/GetOrder | urn:orders:get | the SOAP action existing callers send no longer matches |
| `operation/IOrderService.Cancel` | exists | (absent) | the operation is gone; existing callers that use it will fail |
| `operation/IOrderService.GetOrder/in/GetOrder/region` | (absent) | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders | a new required input; existing callers do not send it |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/id` | xs:int; min=1; max=1; nillable=false; ns=http://example.test/orders | xs:long; min=1; max=1; nillable=false; ns=http://example.test/orders | the type changed from xs:int to xs:long |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/note` | xs:string; min=0; max=1; nillable=true; ns=http://example.test/orders | (absent) | an item that existing callers read is no longer returned |
| `operation/IOrderService.Lookup/in-header/AuthToken` | complex; min=1; max=1; nillable=false; ns=http://example.test/orders | (absent) | an input that existing callers send is no longer in the contract; the new service may reject or ignore it |
| `operation/IOrderService.Lookup/in-header/AuthToken/token` | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders | (absent) | an input that existing callers send is no longer in the contract; the new service may reject or ignore it |
| `operation/IOrderService.Lookup/in/Lookup` | complex; min=1; max=1; nillable=false; ns=http://example.test/orders | complex; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |
| `operation/IOrderService.Lookup/in/Lookup/sku` | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |
| `operation/IOrderService.Lookup/out/LookupResponse` | complex; min=1; max=1; nillable=false; ns=http://example.test/orders | complex; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |
| `operation/IOrderService.Lookup/out/LookupResponse/#order` | sku\|price\|stock | price\|sku\|stock | the members are in a different order; order-sensitive serializers may drop or default them |
| `operation/IOrderService.Lookup/out/LookupResponse/price` | xs:decimal; min=1; max=1; nillable=false; ns=http://example.test/orders | xs:decimal; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |
| `operation/IOrderService.Lookup/out/LookupResponse/sku` | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders | xs:string; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |
| `operation/IOrderService.Lookup/out/LookupResponse/stock` | xs:int; min=1; max=1; nillable=false; ns=http://example.test/orders | xs:int; min=1; max=1; nillable=false; ns=http://example.test/orders/v2 | the XML namespace of the element changed from "http://example.test/orders" to "http://example.test/orders/v2"; callers and the service no longer agree on its name |

## Review (2)

| Where | Old | New | Why |
|---|---|---|---|
| `binding/IOrderService/soap1.1-http[OrderBinding]/policy` | All\|ExactlyOne\|TransportBinding | (absent) | a declared security or messaging policy is gone |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/status/#enum` | Closed\|Open | Closed\|OnHold\|Open | callers may now receive values they were not built for (added OnHold) |

## Compatible (2)

| Where | Old | New | Why |
|---|---|---|---|
| `operation/IOrderService.GetOrder/in/GetOrder/locale` | (absent) | xs:string; min=0; max=1; nillable=false; ns=http://example.test/orders | a new optional input |
| `operation/IOrderService.Ship` | (absent) | exists | a new operation; existing callers do not use it |

## Informational (4)

| Where | Old | New | Why |
|---|---|---|---|
| `endpoint/OrderService/OrderPort` | http://old-host.example.test/OrderService.svc | https://new-host.example.test/orders | the service address differs; that is a deployment detail, not a contract change |
| `endpoint/OrderService/OrderSecurePort` | https://old-host.example.test/OrderService.svc | https://new-host.example.test/orders-secure | the service address differs; that is a deployment detail, not a contract change |
| `operation/IOrderService.GetOrder/in/GetOrder/#order` | (absent) | orderId\|region\|locale | a new ordered list of members |
| `operation/IOrderService.GetOrder/out/GetOrderResponse/GetOrderResult/#order` | id\|status\|note\|lines | id\|status\|lines | members were added at the end or removed; the order of the rest is unchanged |


## What the tool could not read or compare

Security policies are compared only as the names of what they declare, not their settings. Behaviour behind the service is not compared.
