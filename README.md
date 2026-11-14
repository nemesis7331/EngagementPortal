# Cyber Engagement Portal

Portale per la gestione degli engagement di cybersecurity: workflow, documenti,
pianificazione, preparazione, report, revisione, distribuzione ed email.

Applicazione ASP.NET Core (`EngagementPortal.Web`) con deploy automatico su Azure
App Service a ogni push su `main`.

## Funzionalità

- **Ruoli e tenant**: Cliente, Cybersec, Cyberdefence, Manager, Auditor. Il profilo
  Cliente vede solo gli engagement del proprio tenant; i profili provider hanno
  visibilità cross-tenant.
- **Documenti**: scheda tecnica con manleva, documento di ingaggio, ordine, business case.
- **Pianificazione**: proposte di date, accettazione/rifiuto, richiesta di
  riprogrammazione, durata massima 60 giorni.
- **Preparazione**: scope tecnico e credenziali (consegna tramite vault/canale
  separato, nessuna password conservata nel portale).
- **Reportistica**: bozze, revisione del manager, pubblicazione al cliente,
  download tracciato. Il cliente vede solo i deliverable pubblicati.
- **Comunicazioni**: distribution list e email automatiche di inizio, fine e
  consegna deliverables.
- **Solleciti**: regole con soglia in giorni, elenco dei solleciti dovuti e invio.
- **Registro audit**: visibilità differenziata per profilo, esportazione CSV.
- **Dashboard**: endpoint `/api/dashboard/todo` con la prossima azione per ogni
  engagement e profilo.

## Interfaccia

Aprendo l'applicazione (`/`) si usa la SPA completa: dashboard, dettaglio
engagement, stepper del workflow e tutte le schede operative. Il selettore
ruolo/tenant in alto permette di provare i diversi profili (demo).

> Nota: l'identità è simulata tramite parametri `role`/`tenant` per rendere la
> demo navigabile senza configurare Entra ID. In produzione va sostituita con
> l'autenticazione App Service EasyAuth descritta in `documentazione.txt`.

## API

Le API sono esposte sotto `/api` e documentate in `documentazione.txt`
(sezione 2.4). Le principali:

| Metodo | Endpoint |
| --- | --- |
| GET | `/api/me` |
| GET/POST | `/api/engagements` |
| GET | `/api/engagements/{id}` |
| GET | `/api/dashboard/todo` |
| POST | `/api/engagements/{id}/manleva` · `/documento-ingaggio` · `/order` · `/business-case` |
| GET/POST | `/api/engagements/{id}/proposals` |
| POST | `/api/engagements/{id}/proposals/{pid}/decide` |
| GET/PUT | `/api/engagements/{id}/scope` |
| GET/PUT | `/api/engagements/{id}/credentials` |
| GET/POST | `/api/engagements/{id}/reports` |
| POST | `/api/engagements/{id}/reports/{rid}/download` |
| POST | `/api/engagements/{id}/review/submit` · `/review/decide` · `/reports/publish` |
| GET/POST/DELETE | `/api/engagements/{id}/distribution-list` |
| GET | `/api/engagements/{id}/mails` |
| POST | `/api/engagements/{id}/mails/{type}/toggle` · `/send` |
| GET | `/api/engagements/{id}/log` · `/log/export` |
| GET/PUT | `/api/solicitations/rules` · `/rules/{key}` |
| GET | `/api/solicitations/due` |
| POST | `/api/solicitations/{key}/send` |
| GET | `/health` · `/health/ready` |

## Sviluppo locale

```bash
dotnet build EngagementPortal.sln -c Release
dotnet test EngagementPortal.sln -c Release
dotnet run --project EngagementPortal.Web
```

Aprire `http://localhost:5xxx` (la porta è indicata nel log di avvio).

## Documentazione

- `documentazione.txt` — architettura e requisiti di riferimento
- `demo-poc.html` — specifica UX di riferimento
- `improvement.md` — piano di evoluzione
