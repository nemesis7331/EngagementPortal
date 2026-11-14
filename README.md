# Cyber Engagement Portal

Portale per la gestione degli engagement di cybersecurity: clienti e utenze,
workflow documentale, pianificazione, preparazione, report, revisione,
distribuzione ed email.

Applicazione ASP.NET Core (`EngagementPortal.Web`) con deploy automatico su Azure
App Service a ogni push su `main`.

## Funzionalità

- **Ruoli e tenant**: Cliente, PMO, Pentester, Manager, Auditor. Il profilo
  Cliente vede solo gli engagement del proprio tenant; i profili provider (PMO,
  Pentester, Manager, Auditor) hanno visibilità cross-tenant.
- **Clienti e utenze**: il Manager crea e rinomina i clienti, associa o rimuove
  utenze e vede gli engagement collegati. Gli altri profili provider possono
  consultare l'anagrafica.
- **Documenti**: scheda tecnica con manleva, documento di ingaggio, ordine,
  business case.
- **Pianificazione**: proposte di date, accettazione/rifiuto, richiesta di
  riprogrammazione, durata massima 60 giorni.
- **Preparazione**: scope tecnico e credenziali (consegna tramite vault/canale
  separato, nessuna password conservata nel portale).
- **Reportistica**: bozze caricate dal Pentester, revisione del Manager,
  pubblicazione al cliente, download tracciato. Il Cliente vede solo i
  deliverable pubblicati.
- **Comunicazioni**: distribution list e email automatiche di inizio, fine e
  consegna deliverables, gestite dal PMO.
- **Solleciti**: regole con soglia in giorni, elenco dei solleciti dovuti e
  invio, gestiti dal PMO.
- **Registro audit**: visibilità differenziata per profilo, esportazione CSV.
- **Dashboard**: endpoint `/api/dashboard/todo` con la prossima azione per ogni
  engagement e profilo.

## Ruoli e permessi (RACI)

Legenda:

- **R** = *Responsible*: il ruolo esegue l'attività.
- **A** = *Accountable*: il ruolo decide, approva o risponde dell'esito.
- **C** = *Consulted*: il ruolo viene consultato / deve essere informato prima.
- **I** = *Informed*: il ruolo riceve l'informazione o vede il risultato.
- **—** = nessun accesso.

Una stessa attività può avere più **R**: il codice consente l'azione a più ruoli
(ad esempio Cliente e PMO possono entrambi compilare lo scope). La matrice
riporta esattamente i controlli applicati dalle API.

| Attività | Cliente | PMO | Pentester | Manager | Auditor |
| --- | --- | --- | --- | --- | --- |
| Accesso in lettura agli engagement | R (propri tenant) | R | R | A/R | R (sola lettura) |
| Creare clienti | I | I | I | A/R | I |
| Rinominare clienti | I | I | I | A/R | I |
| Aggiungere/rimuovere utenze cliente | I | I | I | A/R | I |
| Consultare clienti, utenze ed engagement associati | R (solo proprio) | R | R | A/R | R |
| Creare engagement | I | R | I | A/R | I |
| Caricare scheda tecnica / manleva | A/R | R (per conto del cliente) | I | I | I |
| Caricare documento di ingaggio, ordine e business case | I | A/R | I | I | I |
| Proporre date o richiedere riprogrammazione | R | A/R | I | I | I |
| Decidere una proposta di date inviata dal provider | A/R | R | I | I | I |
| Decidere una proposta di date inviata dal cliente | R | A/R | I | I | I |
| Compilare scope e credenziali | R | A/R | I | — (credenziali non visibili) | — |
| Caricare report | I | I | A/R | I | I |
| Inviare report in revisione al Manager | I | I | A/R | I | I |
| Approvare o richiedere modifiche ai report | I | I | C | A/R | I |
| Pubblicare i report al cliente | I | I | A/R (dopo approvazione) | C | I |
| Scaricare report (il Cliente solo i pubblicati) | R | R | R | R | R |
| Gestire la distribution list | R (solo destinatari Cliente) | A/R (tutti) | I | I | I |
| Configurare e inviare le email automatiche | I | A/R | I | I | I |
| Configurare e inviare i solleciti | — | A/R | I | I | I |
| Consultare il registro audit | R (eventi visibili) | R | R | R | R |
| Esportare il registro audit in CSV | R | R | R | R | R |

### Cosa può fare ogni ruolo

- **Cliente**: aggiunge informazioni ai propri engagement (manleva, scope,
  credenziali, destinatari Cliente della distribution list), propone o decide le
  date, scarica la reportistica pubblicata. Non vede i dati degli altri tenant.
- **PMO**: coordina gli engagement dei clienti; crea engagement, carica documento
  di ingaggio/ordine/business case e manleva per conto del cliente, gestisce
  pianificazione, scope, credenziali, distribution list completa, email e
  solleciti. Vede le credenziali.
- **Pentester**: consulta gli engagement e le informazioni, carica i report, li
  invia in revisione al Manager e li pubblica al cliente dopo l'approvazione.
  Vede le credenziali; non crea engagement e non gestisce email o solleciti.
- **Manager**: crea e gestisce clienti e utenze, crea engagement, rivede e
  approva i report del Pentester. Non vede le credenziali e non gestisce scope,
  planning, email o solleciti.
- **Auditor**: vede tutto in sola lettura, tranne le credenziali.

### Allineamento con il codice

- Creazione engagement: `POST /api/engagements` consente PMO e Manager.
- Clienti e utenze: `POST/PUT /api/clients...` riservati al Manager; `GET
  /api/clients` filtra il Cliente sul proprio tenant.
- Manleva: `POST /api/engagements/{id}/manleva` consente Cliente e PMO.
- Documento di ingaggio, ordine e business case: solo PMO.
- Proposte: `POST /api/engagements/{id}/proposals` consente Cliente e PMO; la
  decisione spetta alla controparte (`Client` ↔ `PMO`).
- Scope e credenziali: `CanEditPreparation` consente Cliente e PMO, con fasi
  diverse; le credenziali non sono visibili a Manager e Auditor.
- Report: caricamento, invio in revisione e pubblicazione solo Pentester;
  approvazione solo Manager; pubblicazione subordinata a `ReviewStatus.Approved`.
- Scope e credenziali: modificabili solo dopo l'accettazione della pianificazione
  e nelle fasi Prep/Scheduled per il Cliente, Prep/Scheduled/Running per il PMO.
- Distribution list: scrittura Cliente/PMO; il Cliente può gestire solo i
  destinatari con organizzazione `Cliente`.
- Email: toggle e invio solo PMO.
- Solleciti: lettura per i profili provider, scrittura e invio solo PMO; il
  Cliente non ha accesso alle regole.

## Dati e persistenza

L'applicazione **non usa un database**. I dati vivono in memoria tramite
`InMemoryEngagementRepository` e `InMemoryClientRepository`: il seed demo viene
ricreato a ogni avvio e tutte le modifiche (engagement, clienti, utenze, report,
email) si perdono al riavvio.

Il codice è organizzato dietro le interfacce `IEngagementRepository` e
`IClientRepository`, quindi la sostituzione con EF Core/SQL richiede solo una
nuova implementazione e la registrazione in dependency injection.

## Interfaccia

Aprendo l'applicazione (`/`) si usa la SPA completa: dashboard, dettaglio
engagement, stepper del workflow, gestione clienti e tutte le schede operative.
Il selettore ruolo/tenant in alto permette di provare i diversi profili (demo).

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
| GET/POST | `/api/clients` |
| PUT | `/api/clients/{tenantId}` |
| POST/DELETE | `/api/clients/{tenantId}/users` · `/users/{userId}` |
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
