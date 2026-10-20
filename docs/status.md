# Status — udtrækning af services

> Første udkast skrevet af Claude på baggrund af repoets tilstand. Gruppen
> ejer filen: ret, så den passer, og hold den opdateret når et trin er færdigt.
> Læs denne fil ved start af en ny chat.

Sidst opdateret: 2026-10-20

## Order-service (`src/OrderService.api`)

| Trin | Status | Note |
|---|---|---|
| Aggregat kopieret (Order, OrderItem, Address, CatalogItemOrdered) | Færdig | Kortlagt i `docs/order-kortlagning.md` |
| Egen `OrderDbContext` + `InitialCreate`-migration | Færdig | Egen database (database-per-service) |
| `Program.cs`: DbContext registreret (`OrderConnection`) | Færdig | |
| Repository-interfaces (`IRepository<T>` / `IReadRepository<T>`) | Færdig | Branch `feature/order-read-repository` |
| `EfRepository<T>` + DI-registrering i `Program.cs` | Færdig | Bygger; ikke kørt endnu |
| Specifications (`CustomerOrdersSpecification`, `OrderWithItemsByIdSpec`) | Færdig | Kopieret til `Specifications/` |
| `MinimalApi.Endpoint` i csproj | Færdig | Mangler `AddEndpoints()` / `MapEndpoints()` + Swagger i `Program.cs` |
| Read-endpoints (hent ordre, mine ordrer) | I gang (gruppen skriver selv) | Skal designes ud fra handlerne; Order har ingen eksisterende API at kopiere |
| Monolitten kalder servicen (handlers → HTTP-klient) | Ikke startet | Se beslutning nedenfor |
| Routing via API Gateway til OrderService | Ikke startet | Selve strangler-snittet |
| Migrering af eksisterende ordrer fra `CatalogDb` | Ikke afklaret | Se åbne spørgsmål |
| Write-siden (opret ordre) | Ikke startet | Koblet til dual-write/saga, parkeret |

## Catalog-service (`src/CatalogService.api`)

Endpoints (brands, types, items: list, get, create, update, delete) er
oprettet. Repository-interfacet findes, men `Program.cs` mangler endpoint-
registrering, og routing er ikke verificeret. Udfyld.

## Beslutninger

ADR bruges ikke i projektet (en anden i gruppen har ansvar for dokumentation).
Beslutninger noteres her.

| Beslutning | Hvorfor |
|---|---|
| Order får egen database | Krav om database-per-service |
| `CatalogItemOrdered` gemmes som snapshot i ordren | Order skal ikke slå op i Catalog (undgår kobling) |
| **Order udtrækkes som "variant 1": monolitten kalder servicen via API Gateway.** `OrderController` og viewsene bliver i monolitten; `GetMyOrdersHandler` / `GetOrderDetailsHandler` kalder Order-servicens JSON-endpoints (gennem gatewayen) i stedet for repository'et. | Order har i dag kun server-renderet HTML (MVC, `[Authorize]`, cookies/Identity i monolitten) og intet API. Servicen er stadig uafhængig (egen DB, eget API, kan deployes alene); afhængigheden går fra monolit til service. Mindst invasivt, passer til incremental migration, og routing/auth/rate-limiting samles i gatewayen (krav). |
| Forkastet: "variant 2", gateway router `/Order/*` til en service med egne views | Kræver auth (cookie/Identity) og layout duplikeret i servicen og gør den til en mini-webapp; bruger tid, der er brug for til gateway, RabbitMQ, lager, CI/CD og overvågning. Kan genovervejes, hvis UI senere flyttes til en frontend. |
| Kompromis at kunne forklare: gatewayen sidder mellem to server-komponenter (monolit → service), ikke mellem browser og service | Giver ét sted til routing, auth, rate-limiting og senere load balancing |

## Åbne spørgsmål

- Hvordan kommer eksisterende ordrer fra `CatalogDb` over i Order-databasen
  (engangsmigrering, eller læser servicen fra monolitten til at starte med)?
- Hvem er `buyerId`? Order har ikke `User.Identity`; den skal sendes med i
  requesten, og kontrol af at brugeren kun ser egne ordrer ligger i
  monolitten/gatewayen.
- Dual-write ved oprettelse af ordre (besked + DB): **parkeret**. Mulige veje:
  accepter + afbød (publisher confirms, idempotente consumers, saga med
  timeout) eller Outbox (uden for pensum).
- Koreografi vs. orkestrering i checkout-saga: ikke valgt.

## Næste skridt

1. Skriv de to read-endpoints i `OrderService.api` (`GET api/orders/{orderId}`,
   `GET api/orders?buyerId=...`) og sæt `AddEndpoints()`/`MapEndpoints()`/Swagger op.
2. Test dem mod Order-databasen (migration + testdata).
3. Ændr handlerne i monolitten til at kalde servicen via HTTP-klient.
4. Sæt API Gateway op og rut `/api/orders/*` til OrderService.
