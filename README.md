# Cyber Engagement Portal

Portale per la gestione degli engagement di cybersecurity: workflow, documenti,
pianificazione, preparazione, report, revisione, distribuzione ed email.

Applicazione ASP.NET Core (`EngagementPortal.Web`) con deploy automatico su Azure
App Service a ogni push su `main`.

## Sviluppo locale

```bash
dotnet build EngagementPortal.sln -c Release
dotnet test EngagementPortal.sln -c Release
dotnet run --project EngagementPortal.Web
```

## Documentazione

- `documentazione.txt` — architettura e requisiti di riferimento
- `demo-poc.html` — specifica UX di riferimento
- `improvement.md` — piano di evoluzione
