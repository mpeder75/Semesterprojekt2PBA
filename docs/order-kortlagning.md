| Fil | Arver fra/implementerer | Komponerer/indeholder | Bruger NuGet-pakke | Refererer til andre domæner |
|---|---|---|---|---|
| Order.cs | BaseEntity, IAggregateRoot | Address.cs, OrderItem.cs | Ardalis.GuardClauses | nej |
| OrderItem.cs | BaseEntity | CatalogItemOrdered.cs | nej | nej |
| Address.cs | nej | nej | nej | nej |
| CatalogItemOrdered.cs | nej | nej | Ardalis.GuardClauses | nej |

