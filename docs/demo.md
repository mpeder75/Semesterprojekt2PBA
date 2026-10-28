# Demo: butikken og Order-servicen via RabbitMQ

Kører lokalt. Azure har endnu den gamle `main`, så Web dér kan ikke læse ordrer via RabbitMQ, før `feature/monolith-calls-order-service` er merget.

## Før du starter (5 min)

Tre terminaler:

| # | Mappe | Kommando |
|---|-------|----------|
| 1 | repo-roden | `docker compose -f docker-compose.rabbitmq.yml up -d` |
| 2 | `src/OrderService.api` | `dotnet run` |
| 3 | `src/Web` | `dotnet run --launch-profile Web` |

Åbn i browseren, så faner er klar:

- Butikken: <https://localhost:5001/> (login: `demouser@microsoft.com` / `Pass@word1`)
- RabbitMQ-UI: <http://localhost:15672> (guest/guest) -> **Queues**
- Order Swagger: <http://localhost:5233/swagger>

Tjek: i RabbitMQ-UI'et skal du se køerne `order.create`, `order.list` og `order.get`.

## 1. Idéen (1 min)

Eksemplet er Strangler Pattern: Order er trukket ud af monolitten som egen service med egen database. Butikken (Web) kalder den ikke direkte, men over RabbitMQ.

```
Web (monolit) --order.create--> OrderService (kommando)
Web (monolit) --order.list ---> OrderService (request-reply)
Web (monolit) --order.get ----> OrderService (request-reply)
```

Pointe: to slags beskeder.
- **Kommando** ("gør noget"): Web sender og venter ikke på svar.
- **Request-reply** ("giv mig data"): Web sender en request og venter på en reply.

## 2. Oprettelse (kommando) — 3 min

1. Læg en vare i kurven, tryk **Checkout -> Pay Now**.
2. Vis i Swagger: `GET api/orders` med `buyerId = demouser@microsoft.com`. Ordren ligger i Orders egen database.
3. Forklar kæden: `Checkout.cshtml.cs` -> `OrderService.CreateOrderAsync` -> `OrderMessagePublisher` -> kø `order.create` -> `CreateOrderConsumer` -> Order-databasen.

Sig: Web ved ikke, hvor eller hvordan ordren gemmes. Den sender kun en besked (`CreateOrderCommand`).

## 3. Liste (request-reply) — 3 min

1. Åbn **My orders** i butikken.
2. Forklar: Web sender `ListOrdersRequest` på `order.list` med `ReplyTo` og `CorrelationId`. `ListOrdersConsumer` slår ordrerne op og svarer på den midlertidige kø.
3. `CorrelationId` binder reply'en til den rigtige request.

## 4. Detalje (request-reply) — 2 min

1. Klik **Detail** på en ordre (brug ordre 3 eller 4).
2. Samme mønster på `order.get`. `GetOrderResponse.Found` oversættes til `null` i handleren, hvis ordren ikke findes.

## 5. Fejlhåndtering (valgfrit) — 3 min

1. Stop OrderService (Ctrl+C i terminal 2).
2. Tryk **Pay Now** igen. Vis i RabbitMQ-UI'et, at beskeden ligger i `order.create` (kolonnen *Ready*).
3. Start OrderService igen. Beskeden bliver behandlet og ordren oprettet.
4. Forklar: en kommando mistes ikke, men en request har 5 sekunders timeout, så **My orders** fejler, mens servicen er nede.

## Hvad der er lavet (kort)

- Order-service: egen database, repository, specifications, endpoints, tests (18 enhedstests).
- RabbitMQ lokalt i Docker; hosted RabbitMQ hos CloudAMQP til Azure.
- `CreateOrderConsumer`, `ListOrdersConsumer`, `GetOrderConsumer` i Order.
- `RequestReplyClient` (korrelations-id, midlertidig svar-kø, timeout) og `OrderMessagePublisher` i `Infrastructure/Messaging`.
- `GetMyOrdersHandler` og `GetOrderDetailsHandler` i Web bruger klienten i stedet for databasen.
- Azure: broker, database og connection strings sat op for Order.

## Det kan siges, hvis nogen spørger

- **Adressen er hårdkodet** ("123 Main St."); den kommer fra `Checkout.cshtml.cs`.
- **Kurv og katalog** slås stadig op i monolittens `CatalogDb` ved checkout. Det skifter, når Basket og Catalog bliver services.
- **Ingen dead-letter-kø:** afviste beskeder forsvinder.
- **Azure:** Order-servicen kører dér (database + broker virker). Web deployes først efter merge, så demoen kører lokalt.
- **Free-plan (F1):** apps går i dvale efter cirka 20 min uden trafik, så consumeren lytter ikke dér, før appen er vækket.
- **Hvorfor RabbitMQ til læsning?** Gruppens beslutning for ensartet asynkron kommunikation. HTTP havde været enklere til læsning (se `docs/status.md`, Beslutninger).

## Hvis noget går galt

| Problem | Løsning |
|---------|---------|
| `BrokerUnreachableException` ved opstart | RabbitMQ kører ikke: kør `docker compose -f docker-compose.rabbitmq.yml up -d` |
| "My orders" hænger i 5 sek. og fejler | OrderService kører ikke (terminal 2) |
| Ingen kø i RabbitMQ-UI'et | Start OrderService; køerne oprettes ved opstart |
| Detail viser intet | Brug en ordre, der findes i Order-databasen (3 eller 4) |
| `https://localhost:7234` åbner ikke | Order bruger `http://localhost:5233` som standard |
