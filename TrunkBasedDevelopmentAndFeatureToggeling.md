# Trunk Based Development + Feature Toggles

Kort version: Merge tidligt, deploy sikkert, release når I vil.

## Hvorfor det virker
- **(`Trunk Based Development`)**: en hovedbranch (`main`), små hyppige commits, kortlivede branches.
- **(`Feature toggles`)**: ny funktionalitet ligger i trunk men er **OFF** indtil I aktiverer den.
- Resultat: færre merge‑konflikter, hurtigere feedback, lavere release‑risiko.

## Hvordan gør man det?
1. Opret toggle (default `false`).

- (`Hvad er en toggle?`) En konfigureret ON/OFF‑switch (boolean eller rules) der styrer om en ny kodevej bruges i runtime.
- Hvor oprettes den? Som regel i appsettings.json med en dafault værdi.
- **Hvordan merges til main når koden er ufuldstændig?**<br>
    Implementér al kode bag togglen og sæt default til `false`. <br>
    Merge til `main` som normalt — koden bliver compiled og bliver en del af build, men den udføres ikke, fordi togglen er `false` i drift. <br>
    Det betyder I stadig skal teste og reviewe koden, men I undgår at eksponere ufuldstændig funktionalitet.<br>
- Vigtigt: toggles beskytter kun runtime‑adgang, ikke build. Fjern toggle + relateret kode når feature er færdig (cleanup) så I ikke akkumulere dead code.

2. Implementér feature bag togglen.
3. Merge ofte til trunk.
4. Aktivér i staging for testbrugere.
5. Aktivér i produktion når klar.
6. Fjern toggle + gammel kode (cleanup‑dato).

## Konventioner (er det relevant?)
Ja — enkle konventioner gør det meget lettere at automatisere, finde toggles og sikre cleanup. Hold dem korte og håndterbare:

- Navn: `catalog.<feature>.<purpose>` (fx `catalog.newSearch.release`)
- Ejer: én ansvarlig person per toggle
- Cleanup: angiv en 'remove by' dato ved oprettelse (kort horizon, fx 2–4 uger)
- Test: hver toggle skal have tests for både **ON** og **OFF** scenarier

Disse regler forhindrer, at toggles bliver permanent teknisk gæld.

***

## .NET 8: Toggle opsætning

### 1) `appsettings.json`
appsettings holder hvad featureflags værd skal være af default. Remote stores kan overskrive disse pr. miljø.

```json
{
  "FeatureFlags": {
    "NewCatalogEndpoint": false
  }
}
```

### 2) `FeatureFlags.cs`
Simpel class som binder værdier fra konfiguration til typed access.

kan placeres i en service (`Catalog.Api/Configuration/FeatureFlags.cs`)

```csharp
public class FeatureFlags 
{ 
    public bool NewCatalogEndpoint { get; set; }
}
```

### 3) `Program.cs` (registrering)
Her binder I config til IOptions og tilføjer controllers. IOptions bruges til at læse flag ved runtime.

```csharp
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FeatureFlags>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
```

### 4) Minimal API eksempel (hvad det viser)
Viser runtime‑grening — code er compiled uanset toggle, men kun køres når flag er true.

```csharp

• app.MapGet("/products", ... ) opretter en Minimal API GET‑endpoint på ruten /products.
• Parameterne (IOptions<FeatureFlags> flags, CatalogDbContext db) injiceres via DI:
• IOptions<FeatureFlags> giver adgang til jeres konfigurerede feature‑flags (flags.Value).
• CatalogDbContext er EF Core DbContext til databaseadgang.
• if (flags.Value.NewCatalogEndpoint) kontrollerer om toggle er slået til:
• Hvis true: kør den filtrerede query db.Products.Where(p => p.Price > 0).ToListAsync() — returnerer kun produkter med pris > 0.
• Hvis false: kør db.Products.ToListAsync() — returner alle produkter.
• await bruges fordi EF Core kører asynkront; Results.Ok(...) pakker resultatet i et HTTP 200 OK‑svar.
• Vigtigt at bemærke:
  • Koden bag togglen bliver kompilere uanset toggle‑værdien — togglen styrer kun hvilken vej der eksekveres ved runtime. Derfor skal alt kode bygge og tests være grønne, selvom togglen er false.
  • Hvis du vil kunne opdatere flag uden genstart i udvikling, brug IOptionsSnapshot<FeatureFlags> (gives nye værdier per request).
  • Sørg for, at eventuelle nye DB‑ændringer/migrationer håndteres, fordi koden stadig kan referere nye entiteter selvom togglen er off.

app.MapGet("/products", async (IOptions<FeatureFlags> flags, CatalogDbContext db) =>
{
    if (flags.Value.NewCatalogEndpoint)
        return Results.Ok(await db.Products.Where(p => p.Price > 0).ToListAsync());

    return Results.Ok(await db.Products.ToListAsync());
});
```

### 5) Controller eksempel (hvad der er forskellen)
IControllers kan bruge IOptionsSnapshot for at få opdaterede værdier per request (praktisk i scoped services).

```csharp
[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly IOptionsSnapshot<FeatureFlags> _flags;
    private readonly CatalogDbContext _db;

    public ProductsController(IOptionsSnapshot<FeatureFlags> flags, CatalogDbContext db)
    {
        _flags = flags;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (_flags.Value.NewCatalogEndpoint)
            return Ok(await _db.Products.Where(p => p.Price > 0).ToListAsync());

        return Ok(await _db.Products.ToListAsync());
    }
}
```

### 6) (Valgfrit) middleware til observability
Middleware kan tilføje headers eller logs, så I kan se hvilke toggles der var aktive for en request.

```csharp
public class FeatureToggleMiddleware
{
    private readonly RequestDelegate _next; private readonly FeatureFlags _flags;
    public FeatureToggleMiddleware(RequestDelegate next, IOptions<FeatureFlags> flags) { _next = next; _flags = flags.Value; }
    public Task InvokeAsync(HttpContext ctx) { ctx.Response.Headers["X-FF-NewCatalog"] = _flags.NewCatalogEndpoint.ToString(); return _next(ctx); }
}
// app.UseMiddleware<FeatureToggleMiddleware>();
```

## CI/CD og miljø
- Env override: `FEATUREFLAGS__NEWCATALOGENDPOINT=true`
- Kør tests for begge modes: ON/OFF
- Remote toggles ved behov: Azure App Configuration, Unleash, LaunchDarkly

## Quick checklist (uge 1)
- [ ] Opret `FeatureFlags` og bind i `Program.cs`
- [ ] Pak én endpoint ind i en toggle
- [ ] Tilføj tests for ON/OFF
- [ ] Aktivér toggle i staging via env var
- [ ] Demo: OFF -> ON -> cleanup plan
