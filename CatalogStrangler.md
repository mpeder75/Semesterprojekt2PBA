# Catalog Strangler — praktisk template (start her)

Formål: en direkte, praktisk fremgangsmåde I kan følge når I vil udtrække Catalog. Ingen lange analyser — kun trin I kan gøre i kode samme dag.

### Overordnet rækkefølge (kort):
- Stop 0 -> vælg scope
 1. Vælg entiteter 
 2. Kopiér modeller
 3. Lav DbContext/infra
 4. Lav read‑endpoints
 5. Wrap med toggle
 6. Test & staging
 7. Cutover & cleanup

Detaljeret step‑by‑step (følg i rækkefølge)

### Step 0: vælg scope
- Beslut: Start med de vigtigste entiteter for Catalog (fx CatalogItem, CatalogBrand, CatalogType).<br>
    ### Ikke alt på én gang.

### Step 1: entiteter (hurtig, praktisk)
- Åbn filerne i monolitten (fx CatalogItem.cs, CatalogBrand.cs). Kopiér kun POCO/egenskaber og eventuelle små value‑objects I bruger direkte.
- Gem dem i det nye serviceprojekt (Catalog.Core). Ingen forretningslogik eller afhængigheder — kun dataformen.

#### Hvorfor starte her?
- Entiteter er kilden til schema og API‑kontrakter. Når I har modellerne kan I hurtigt lave DbContext og endpoints.

### Step 2: infrastructure / DbContext (kort)
- Opret en minimal CatalogDbContext i Catalog.Infrastructure med DbSet for de entiteter I kopierede.
- Beslut DB‑strategi NU (se Step 3). Hvis I vil kode‑first: opret migrations i dette projekt.

### Step 3: DB‑strategi (praktisk valg)
- Hurtigst (lav risiko): læs direkte fra monolit‑DB (read‑only). Ingen migrations, hurtig demo.
- Renere (code‑first): opret separat DB for Catalog, lav migration + backfill. Brug kun hvis I vil arbejde med schema uafhængigt.

### Step 4: read‑endpoints (hurtig win)
- Implementér simple GET endpoints: list og get by id (fx /products, /products/{id}). Hold payloads som DTO’er der matcher de kopierede entiteter.
- Kør og verifikér at endpointet returnerer data (enten fra monolit DB eller ny DB).

### Step 5: feature toggle (sikker integration)
- Wrap ny kode bag en toggle: default OFF i main. Når OFF, lad kald gå til monolit eller eksisterende implementation.
- Merge tidligt: koden er i repo, builds, men ikke aktiv i produktion før I tænder togglen.

### Step 6: tests & integration
- Skriv et par kontrakttests: kald endpointet med toggle OFF (monolit) og ON (ny service). Sørg for at begge adfærd er dækket.

### Step 7: staging cutover
- Aktivér togglen i staging, test fuldt flow.
- Hvis I bruger egen DB: kør backfill og validér data‑konsistens.

### Step 8: production rollout og cleanup
- Rul ud gradvist: slå toggle ON for et subset eller manuelt på en instans.
- Når stabil: fjern toggle og gammel monolit‑kode (cleanup commit).

### Praktiske tips og korte beslutningsregler
- Start fra entiteter → fordi det giver jer schema og DTO’er hurtigt.
- Endpoints kan laves meget hurtigt efter entiteter; det er ofte bedst for demo/feedback.
- Hold write i monolitten i starten — flyt read først.
- Brug toggles som sikkerhed: merge tidligt, aktiver senere.
- Hvis i er usikre på DB‑ændringer: læs fra monolit DB først.

### *Kort checkliste I kan følge*
- Kopiér CatalogItem, CatalogBrand, CatalogType til Catalog.Core
- Opret CatalogDbContext (kun DbSet for disse)
- Lav GET /products og GET /products/{id} i Catalog.Api
- Bind FeatureFlags og wrap endpointet i toggle (default false)
- Kør lokalt og demo for teamet (toggle off => ingen ændring)

### Skal Catalog være en service eller flere?
- Start med én Catalog‑service. Det giver jer hurtig fremdrift og lav kompleksitet.
- Design intern modularitet (f.eks. produkter, priser, lager) så I senere kan splitte uden rewrite.
- Split først når der er klare tegn: forskellig skalering, forskellige ejere/release‑rytmer eller stærkt forskellige consistency‑krav.

### Hvad gør I efter dette
- Iterér: tilføj POST/PUT når read er stabil
- Planlæg write cutover (dual‑write eller events) når I er klar
