# Cyber Engagement Portal — Improvement Plan

> **Obiettivo:** portare l'MVP attualmente deployato (`EngagementPortal.Web`, ASP.NET Core 10, API minimal + `wwwroot/index.html`) alla parità funzionale con `demo-poc.html`, rispettando l'architettura di `documentazione.txt`, **senza mai rompere il deploy automatico su `main`**.
>
> **Fonte di verità funzionale:** `demo-poc.html` (UX, macchina a stati, regole, testi).
> **Fonte di verità architetturale:** `documentazione.txt` (§1–§5).
> **Stato live verificato:** l'endpoint lista risponde correttamente con ruolo `Pentester` e tenant interno, restituendo 3 engagement seed (Contoso/Fabrikam).

---

## 0. Naming, brand e regole di ingaggio

### 0.1 Naming ufficiale (vincolante)

Il prodotto si chiama **Cyber Engagement Portal**. Questi termini sono obbligatori in UI, codice, database, API, email, log e documentazione del prodotto:

| Concetto | Termine ufficiale | Note |
|---|---|---|
| Nome prodotto | **Cyber Engagement Portal** | Titolo pagina, header, `README.md`, titolo OpenAPI, oggetto email, metadati |
| Azienda fornitrice | **Provider** | Mai il nome dell'azienda; ruoli interni: `Provider PMO`, `Provider Pentester` |
| Documento di autorizzazione all'attività | **manleva** | Sostituisce la sigla storica usata nella demo |
| Documento di ingaggio | **documento di ingaggio** | Sostituisce la sigla storica usata nella demo |
| Ordine | **ordine** | Invariato |
| Business Case | **business case** | Invariato |
| Ruoli | `Client`, `PMO`, `Pentester`, `Manager`, `Auditor` | Invariati |

Regole:
- **Nessun riferimento all'azienda fornitrice** (nome, dominio email, owner, testi, seed, commenti). Usare `Provider` / `provider.example`.
- **Nessuna sigla storica** per manleva e documento di ingaggio: solo i termini ufficiali.
- `demo-poc.html` e `documentazione.txt` restano **immutati** come riferimento: il prodotto non ne eredita la terminologia.
- Prima del merge di ogni step, un test statico verifica l'assenza dei termini vietati nel codice del prodotto (esclusi i file di riferimento).

### 0.2 Regole di ingaggio (valide per ogni step)

1. **Un passo = un commit/PR su `main`** che compila, passa `dotnet test` e viene verificato sul sito live. Mai push di lavoro a metà.
2. **La pipeline resta quella attuale** (`.github/workflows/main_cyber-engagement-demo.yml`): restore → build Release → test → publish → deploy `azure/webapps-deploy@v3` con `startup-command: dotnet EngagementPortal.Web.dll`.
3. **Nessun segreto nel repo**: connection string, client id, SMTP/Graph, Key Vault → App Settings / Key Vault references.
4. **Feature flags in `appsettings.json`**: ogni funzionalità nuova nasce spenta (`Features:*`) e viene accesa dopo verifica. Il fallback in-memory resta disponibile finché EF non è stabile.
5. **Compatibilità retroattiva dell'endpoint di verifica deploy**: finché la CI usa `curl /api/engagements?role=...`, quell'endpoint deve continuare a rispondere in modalità demo. Lo step 0.3 sposta la verifica su `/health`.
6. **Local-first**: `dotnet build EngagementPortal.sln -c Release` e `dotnet test` prima di ogni push.
7. **Rollback**: `git revert <sha>` + push (redeploy in ~1 min). Per il DB: migrazioni additive only (no drop), backup prima di ogni fase che tocca lo schema.
8. **Free tier F1**: niente `Always On`, quota CPU 60 min/giorno, cold start. Evitare job pesanti in-process e websocket (motivo per cui **non** si parte con Blazor Server, vedi §2).

---

## 1. Gap analysis: dove siamo vs dove dobbiamo arrivare

### 1.1 Codice attuale (mappato)

| Area | File | Stato |
|---|---|---|
| Host/API | `EngagementPortal.Web/Program.cs` | Minimal API in un unico file, ~10 endpoint |
| Dominio | `EngagementPortal.Web/Domain/Models.cs` | Solo `Engagement` + `Proposal` + `ReportFile` + `AuditEntry`; mancano ~10 entità |
| Workflow | `Services/EngagementWorkflow.cs` | FSM + `TodoResolver` parziali, nessun test |
| Persistenza | `Services/InMemoryEngagementRepository.cs` | Singleton in RAM, 3 seed, dati persi a ogni restart |
| UI | `wwwroot/index.html` | Dashboard liste + dump JSON del dettaglio; nessuna scheda, nessuno stepper |
| Test | `EngagementPortal.Tests/UnitTest1.cs` | Test vuoto |
| Deploy | `.github/workflows/main_cyber-engagement-demo.yml` | Funzionante, verifica su endpoint API con query param |

### 1.2 Gap per macro-area

| # | Area | Demo / Documentazione | Oggi | Impatto |
|---|---|---|---|---|
| G1 | Autenticazione | Entra ID multitenant, app roles, `tid` → tenant | Ruolo/tenant da **query string** (`?role=`, `?tenant=`), header EasyAuth letti male (`X-MS-CLIENT-PRINCIPAL-IDP` non è il tenant) | Critico: chiunque può impersonare un ruolo/cliente |
| G2 | Autorizzazione | Policy per endpoint + query filter + RLS | Solo `if (role is ...)` sparsi; `Results.Forbid()` **senza schema auth registrato** → rischio 500 invece di 403 | Critico |
| G3 | Persistenza | Azure SQL + EF Core, multi-tenant, RLS | In-memory, nessun EF, nessuna migrazione | Bloccante per uso reale |
| G4 | Entità | `Document` (manleva/documento di ingaggio/ordine/business case), `Scope`, `Credentials`, `Review`, `DistributionListEntry`, `MailConfig/History`, `SolicitationRule/History`, `TenantClientMapping`, `UserRole` | Solo flag `HasManleva`/`OrderConfirmed`, `ScopeDone`/`CredentialsDone`, review come enum | Bloccante: metà dei dati demo non è rappresentabile |
| G5 | Macchina a stati | 7 fasi, 6 step UI, precedenze demo | FSM esiste ma con **precedenze diverse** (vedi §1.3) | Bug di stato visibile |
| G6 | TodoResolver | 9 regole demo, tab di destinazione distinti (scope vs cred) | Implementato in parte, tab unico `prep`, nessun test | UX errata |
| G7 | API | ~35 endpoint (§2.4 doc) | ~10 endpoint | UI non realizzabile |
| G8 | UI | 1 lista con KPI + tabella + 5 tab + 3 chip + modali + toast + stepper + legenda + draft | Solo card + `JSON.stringify(audit)` | Il gap più visibile |
| G9 | File | Blob + SAS 15 min + watermark + audit download | Nessun upload reale (solo flag/`ReportFile` finto) | Requisito di sicurezza |
| G10 | Email | 3 trigger, toggle, invio manuale, storico | Assenti | Funzionalità mancante |
| G11 | Solleciti | 6 regole, soglie, job schedulato, storico | Assenti | Funzionalità mancante |
| G12 | Audit | Append-only, visibilità `All`/`ProviderOnly`, `ClientText`, export CSV | Lista in memoria, filtro visibilità presente, nessun export, mutabile | Requisito di compliance |
| G13 | Test | Unit + integration + E2E | Vuoti | Nessuna rete di sicurezza |
| G14 | Osservabilità | Log/App Insights, health check | Nessun `/health`, nessuna telemetria | Deploy fragility |
| G15 | Branding | **Cyber Engagement Portal**, termini neutri | Titolo vecchio, seed/testi con termini storici e nome fornitore | Da correggere subito (step 0.9) |

### 1.3 Divergenze puntuali già visibili (da correggere con test)

1. **Precedenza fasi** — demo `phase()`: `docs → planning → done(se esiste report pubblicato) → reporting(se oggi > end) → prep → running/scheduled`. Codice attuale: `docs → planning → prep → scheduled → running → reporting → done`. Effetto: un engagement scaduto con prep incompleta oggi risulta `prep`, nella demo `reporting`; un engagement con report pubblicato ma date future oggi risulta `scheduled`, nella demo `done`.
2. **TodoResolver** — demo distingue `scope` e `cred` con testi/azioni diverse; oggi un solo `prep`/"Fornisci scope e credenziali". Demo esclude l'Auditor; oggi non c'è guard esplicito (innocuo ma da allineare).
3. **Enum serializzati come interi** (`phase:1`, `type:0`): la demo usa stringhe leggibili; serve `JsonStringEnumConverter`.
4. **`Results.Forbid()`** senza `AddAuthentication`/handler: da verificare e sostituire con `Results.StatusCode(403)` finché l'auth non è attiva.
5. **`AcceptedProposal`** = primo `Accepted`; la demo ammette `replaced`/`superseded` e sostituzione su riprogrammazione: serve una regola esplicita.
6. **Upload** — oggi `POST /manleva` e `POST /reports` accettano solo metadati, nessun file: la UI demo mostra file, dimensioni, download.

---

## 2. Decisioni architetturali da confermare (e default proposti)

| Decisione | Default proposto | Motivo |
|---|---|---|
| Frontend | **SPA statica servita da ASP.NET** (HTML/CSS/JS modulare, stesso design della demo) + API JSON | È la via più rapida e a rischio zero su F1; la demo è già HTML/JS; Blazor Server richiede websocket e CPU, Blazor WASM aggiunge build/toolchain. Migrazione a Blazor possibile in seguito dietro gli stessi endpoint |
| Persistenza | **EF Core + Azure SQL** (serverless free tier per demo) con **fallback in-memory** se manca la connection string | Rispetta il doc, non blocca il deploy se SQL non è configurato |
| Auth | **EasyAuth App Service** multitenant + parsing `X-MS-CLIENT-PRINCIPAL`; `Microsoft.Identity.Web` solo se serve step-up | Meno codice, zero costi, allineato a §3 del doc |
| File | **Azure Blob + SAS 15 min**; in demo senza storage, filesystem locale + token firmato | Costi minimi, percorso di upgrade pulito |
| Email | **Microsoft Graph** (o SendGrid) dietro `IEmailSender`; in demo `LoggingEmailSender` | Disaccoppia il provider |
| Job | **`IHostedService`** con tick giornaliero idempotente (Azure Functions timer in fase successiva) | Nessun costo extra, sufficiente per demo |
| Multi-tenancy | Shared DB/shared schema + global query filter; RLS in fase 9 | Come da doc §2.2 |
| Dati demo | Seed identico a `demo-poc.html` (9 engagement, 2 clienti, DL, mail, log) con termini ufficiali | Permette confronto 1:1 demo ↔ portale |

> **Azione:** confermare o modificare queste 8 righe prima della Fase 1. Il resto del piano assume i default.

---

## 3. Architettura target del repository

```
EngagementPortal.Web/
  Program.cs                     # composition root sottile
  Domain/                        # entità, enum, regole pure (no EF)
    Models.cs                    # entità complete (§4)
    Roles.cs, Policies.cs
  Data/
    PortalDbContext.cs           # DbSets, query filter, configurazioni
    Configurations/*.cs
    Migrations/
    Seed/DemoDataSeeder.cs       # dataset demo-poc, termini ufficiali
  Auth/
    ClaimsPrincipalParser.cs     # EasyAuth -> UserContext
    TenantProvider.cs            # ITenantProvider
    AuthorizationPolicies.cs
    DemoAuthMiddleware.cs        # fallback ?role/?tenant solo in DemoMode
  Services/
    EngagementWorkflow.cs        # FSM (pura, testabile)
    TodoResolver.cs
    ProposalService.cs
    PreparationService.cs
    ReportWorkflowService.cs
    DistributionListService.cs
    MailService.cs               # IEmailSender + trigger
    SolicitationService.cs
    AuditService.cs
    Storage/IBlobStorage.cs      # SAS / upload / watermark
  Endpoints/
    EngagementEndpoints.cs       # mappa /api/engagements...
    DocumentEndpoints.cs         # manleva, documento di ingaggio, ordine, business case
    PreparationEndpoints.cs
    ReportEndpoints.cs
    CommunicationEndpoints.cs
    SolicitationEndpoints.cs
    DashboardEndpoints.cs
    MeEndpoints.cs
    HealthEndpoints.cs
  Jobs/SolicitationWorker.cs
  wwwroot/
    index.html                   # shell "Cyber Engagement Portal"
    css/app.css                  # palette e componenti demo
    js/api.js, js/state.js, js/views/*.js, js/components/*.js
EngagementPortal.Tests/
  Unit/WorkflowTests.cs, TodoResolverTests.cs, ProposalRulesTests.cs, AuditTests.cs
  Integration/ApiFactory.cs, TenantIsolationTests.cs, WorkflowE2ETests.cs
  Guard/NamingGuardTests.cs      # vieta i termini non ammessi
```

---

## 4. Modello dati target (allineato a §1.2 e §2.5, con naming ufficiale)

Entità e campi minimi da implementare (tutte con `Id`, `TenantId` dove indicato):

- **Engagement**: `Id` (`ENG-YYYY-NNN`), `TenantId`, `ClientId/ClientName`, `Name`, `Type`, `Owner`, `CreatedDate`, `Status`.
- **Document**: `Type` (`Manleva|DocumentoIngaggio|Order|Bc`), `FileRef`, `Date`, `Ok`, `Ref`. Sostituisce i flag `HasManleva`/`OrderConfirmed`.
- **Proposal**: `From`, `By`, `Kind` (`initial|resched`), `StartDate`, `EndDate`, `Note`, `Status`, `Reason`, `Date`.
- **Scope**: `Host`, `IpRange`, `SwaggerFileRef`, `OutOfScope`, `Window`, `Contact`, `Notes`, `Done`, `By`, `Date`.
- **Credentials**: `Vpn`, `Users`, `DeliveryMethod`, `Notes`, `Done`, `By`, `Date` (**mai password**, solo `SecretReference`).
- **Report**: `Name`, `FileRef`, `Date`, `Type`, `Size`, `Published`.
- **Review**: `Status`, `By`, `Date`, `Comment` (entità 1:1, non enum su Engagement).
- **DistributionListEntry**: `Name`, `Email`, `Role`, `Organization` (`Cliente|Provider PMO|Provider Pentester`).
- **MailConfig**: `Type` (`start|end|deliver`), `Auto`, `SentDate`.
- **MailHistory**: `Type`, `SentDate`, `Recipients`, `Auto`, `Visibility`.
- **AuditLogEntry**: `Date`, `Time`, `Author`, `AuthorRole`, `Text`, `Visibility`, `ClientText` — **append-only** (nessun update/delete esposto).
- **SolicitationRule**: `Key` (`manleva|order|prop|prep|rep|rev`), `Enabled`, `ThresholdDays`, `Recipient`, `Subject`, `Description`.
- **SolicitationHistory**: `EngagementId`, `RuleKey`, `SentDate`, `Recipient`, `Detail`, `Auto`, `IdempotencyKey`.
- **TenantClientMapping**: `EntraTenantId`, `ClientId`, `OnboardedDate`, `Approved`.
- **UserRole**: `UserId`, `EntraTenantId`, `Role`, `ClientId?`.
- **Enums**: `AuditVisibility` = `All | ProviderOnly`.

Regole tecniche:
- `DateTimeOffset` UTC ovunque; `DateOnly` per le date di pianificazione.
- Row version/concurrency token su `Engagement`.
- Global query filter `TenantId` + `IsDeleted` (soft delete) su tutto il tenant-scoped.
- Indici: `Engagement(TenantId, Id)`, `AuditLog(EngagementId, Date)`, `Proposal(EngagementId, Status)`, `SolicitationHistory(IdempotencyKey)` unique.
- Migrazioni **additive**; seed demo idempotente.

---

## 5. API target (superficie completa, da §2.4, con naming ufficiale)

Legenda stato: ✅ esiste · ⚠️ parziale · ❌ manca.

| Metodo/Path | Stato | Policy minima |
|---|---|---|
| `GET /health` | ❌ (step 0) | anonimo |
| `GET /api/me` | ⚠️ (da query param → claims) | autenticato |
| `GET /api/engagements` | ✅ (da rifare su EF) | autenticato, filtro tenant |
| `GET /api/engagements/{id}` | ✅ (da rifare) | autenticato + tenant |
| `POST /api/engagements` | ❌ | Provider |
| `POST /api/engagements/{id}/manleva` | ⚠️ metadati | Client/Provider |
| `POST /api/engagements/{id}/documento-ingaggio` | ❌ | Provider |
| `POST /api/engagements/{id}/order` | ⚠️ | Provider |
| `POST /api/engagements/{id}/business-case` | ❌ | Provider |
| `GET /api/engagements/{id}/proposals` | ⚠️ incluso nel dettaglio | autenticato |
| `POST /api/engagements/{id}/proposals` | ✅ | canWrite |
| `POST /api/engagements/{id}/proposals/{pid}/decide` | ✅ | canDecide |
| `GET/PUT /api/engagements/{id}/scope` | ❌ (solo flag) | Client/Pentester |
| `GET/PUT /api/engagements/{id}/credentials` | ❌ (solo flag) | Client/Pentester |
| `GET /api/engagements/{id}/reports` | ⚠️ | autenticato (cliente: solo pubblicati) |
| `POST /api/engagements/{id}/reports` | ⚠️ metadati | Pentester |
| `POST /api/engagements/{id}/reports/{rid}/download` | ❌ | Pentester/Provider + step-up |
| `POST /api/engagements/{id}/review/submit` | ✅ | Pentester |
| `POST /api/engagements/{id}/review/decide` | ✅ | Manager |
| `POST /api/engagements/{id}/reports/publish` | ✅ | Pentester + approved |
| `GET/POST /api/engagements/{id}/distribution-list` + `DELETE .../{entryId}` | ❌ | canWrite / regole DL |
| `GET /api/engagements/{id}/mails` + `POST .../{type}/toggle` + `.../send` | ❌ | Pentester (lettura a tutti) |
| `GET /api/engagements/{id}/log` + `GET .../log/export` | ⚠️ incluso nel dettaglio / ❌ export | autenticato, visibilità |
| `GET /api/solicitations/rules`, `PUT .../{key}`, `GET /due`, `POST /{key}/send` | ❌ | PMO write, altri read |
| `GET /api/dashboard/todo` | ✅ | autenticato |

Convenzioni API:
- `JsonStringEnumConverter`; DTO espliciti (no entità EF serializzate); `ProblemDetails` per errori.
- OpenAPI (`Microsoft.AspNetCore.OpenApi`) su `/openapi/v1.json` in Development, titolo **Cyber Engagement Portal**.
- Paginazione/filtri su lista (`?client=&phase=&q=`), ordinamento stabile.
- Nessun endpoint espone `SecretReference` o dati credenziali a Manager/Auditor/PMO.

---

## 6. Piano di esecuzione a fasi

Ogni fase elenca step atomici, con criteri di accettazione e impatto deploy. Le stime sono per 1 sviluppatore full-time.

### Fase 0 — Guardrail, branding e deploy safety (1–2 giorni)

- [ ] **0.1** Aggiungere `GET /health` (anonimo) che risponde `{ status, version, commit, time }`; `GET /health/ready` con check DB (quando esisterà).
- [ ] **0.2** Aggiungere `AddProblemDetails()`, `UseExceptionHandler`, logging strutturato, `JsonStringEnumConverter`.
- [ ] **0.3** Aggiornare la verifica della workflow: da `/api/engagements?role=...&tenant=...` a `/health`, mantenendo temporaneamente anche il check legacy.
- [ ] **0.4** Introdurre `Features` in `appsettings.json` (`DemoMode=true`, `UseDatabase=false`, `UseEasyAuth=false`, `UseBlob=false`).
- [ ] **0.5** Correggere `Results.Forbid()` → helper `Forbidden()` che non richiede handler auth.
- [ ] **0.6** Introdurre `IDateProvider` (ora reale; override demo) e usarlo in FSM.
- [ ] **0.7** Primi test: FSM sui casi demo + `TodoResolver` (fissano il comportamento **prima** di riscriverlo).
- [ ] **0.8** Abilitare `dotnet format`/analizzatori e `TreatWarningsAsErrors` (facoltativo, non bloccante).
- [x] **0.9** **Rinominazione e branding** (completato localmente, non ancora pushato; build+test verdi, smoke test su lista e `/manleva`):
  - `wwwroot/index.html`: titolo, header e testi → **Cyber Engagement Portal**.
  - `Domain/Models.cs`: `HasManleva`, `AuditVisibility.ProviderOnly`, `Roles.IsProvider()`; nessun nome fornitore.
  - `InMemoryEngagementRepository`: owner `Provider Pentester`, clienti demo, testi log con "manleva" e "documento di ingaggio".
  - Testi UI/API: "Firma e carica la manleva", "documento di ingaggio disponibile", "Registra ordine".
  - `README.md` → titolo **Cyber Engagement Portal** + descrizione breve.
  - `NamingGuardTests`: fallisce se nel codice del prodotto (Web, workflow, README) compaiono i termini vietati (nome fornitore, sigle storiche). Esclusi `demo-poc.html`, `documentazione.txt` e la cartella `Guard` stessa.
- [ ] **0.10** Seed demo allineato al naming ufficiale (le 9 engagement della demo, con ruoli/org `Provider PMO`/`Provider Pentester`).
- [ ] **0.11** Igiene repository: rimuovere `bin/` e `obj/` dal versionamento (`git rm -r --cached EngagementPortal.Web/bin EngagementPortal.Web/obj EngagementPortal.Tests/bin EngagementPortal.Tests/obj`) e aggiungere `.gitignore` (`bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`). Verificare che la pipeline continui a pubblicare da sorgente.

**Accettazione:** il sito live risponde a `/health` 200; deploy verde; endpoint legacy invariato; UI mostra **Cyber Engagement Portal**; `dotnet test` con ≥8 test verdi + naming guard verde.
**Rischio deploy:** nullo (solo aggiunte e rinomina testuale).

### Fase 1 — Dominio completo + EF Core + seed demo (3–5 giorni)

- [ ] **1.1** Riscrivere `Domain/Models.cs` con tutte le entità del §4 (record/classi immutabili dove possibile), mantenendo le regole pure nel dominio.
- [ ] **1.2** Aggiungere pacchetti `Microsoft.EntityFrameworkCore.SqlServer` (e `InMemory`/`Sqlite` per i test).
- [ ] **1.3** Creare `PortalDbContext`, configurazioni Fluent, query filter tenant, indici, concurrency token.
- [ ] **1.4** Prima migrazione `InitialCreate`; script SQL versionato in `Data/Migrations`.
- [ ] **1.5** Implementare `EfEngagementRepository` dietro la stessa `IEngagementRepository` (interfaccia estesa), con `AsNoTracking` in lettura.
- [ ] **1.6** Implementare `DemoDataSeeder` con **gli stessi 9 engagement, DL, mail e log della demo** (Nova Bank, Orion Energia), idempotente e con naming ufficiale.
- [ ] **1.7** Selezione runtime: `UseDatabase=true` → EF; altrimenti `InMemoryEngagementRepository` (seed demo). Migrazione automatica all'avvio **con retry** e feature flag; in caso di errore log + fallback (non crash) in DemoMode.
- [ ] **1.8** Refactor `EngagementWorkflow` per leggere le nuove entità; allineare la precedenza delle fasi alla demo (test-first sui 6 casi: G5).
- [ ] **1.9** Test di integrazione con `WebApplicationFactory` + SQLite/InMemory: CRUD, query filter, concorrenza.

**Accettazione:** con `UseDatabase=false` il comportamento è identico a oggi; con `UseDatabase=true` (DB locale) la lista restituisce i 9 engagement demo; test verdi.
**Rischio deploy:** medio → mitigato dal fallback in-memory e dalla migrazione non distruttiva.

### Fase 2 — Autenticazione e autorizzazione (2–4 giorni, include configurazione Azure)

- [ ] **2.1** `ClaimsPrincipalParser`: decodifica base64 di `X-MS-CLIENT-PRINCIPAL`, estrae `name`, `email`, `oid`, `tid`, ruoli (`roles`/`groups`) e costruisce `UserContext`.
- [ ] **2.2** `ITenantProvider` + `TenantClientMapping` (onboarding al primo accesso, `Approved` per demo).
- [ ] **2.3** `DemoAuthMiddleware`: se `Features:DemoMode=true` **e** la richiesta non è autenticata, accetta `?role=&tenant=` (comportamento attuale) per non rompere demo e CI. In produzione `DemoMode=false` → 401.
- [ ] **2.4** Policy: `ClientOnly`, `ProviderOnly`, `CanWrite`, `CanSeeCredentials`, `CanManageMail`, `CanManageSolicitations`, `ManagerReview`, `CanDecideProposal` (handler dedicato), `ReportAccess` (step-up placeholder).
- [ ] **2.5** Applicare `[Authorize(Policy=...)]`/`.RequireAuthorization(...)` a **tutti** gli endpoint; test matrice ruolo × endpoint (atteso 200/403).
- [ ] **2.6** Configurazione Azure (fuori repo, documentata in `docs/azure-auth.md`): App Service → Authentication → Microsoft → *Any Microsoft Entra directory – Multitenant*; issuer `.../common/v2.0`; app roles; `/.auth/me` per debug.
- [ ] **2.7** Tenant isolation test: cliente A non vede/scrive dati di B (lista, dettaglio, proposte, report, DL, log).
- [ ] **2.8** Sostituire la verifica 2FA finta: in demo resta il gate dietro flag; in produzione `WithStepUp()`/Conditional Access.

**Accettazione:** in DemoMode tutto funziona come oggi; con `DemoMode=false` senza token → 401, con claims ruolo corretto → 200; matrice permessi verde.
**Rischio deploy:** alto → attivare `DemoMode=false` solo dopo aver verificato EasyAuth sul live; la CI usa `/health` (step 0.3).

### Fase 3 — Completamento API + validazioni (3–5 giorni)

- [ ] **3.1** Spezzare `Program.cs` in endpoint group (§3); DTO di request/response per ogni area.
- [ ] **3.2** Implementare gli endpoint ❌ del §5 (documento di ingaggio, business case, scope, credentials, DL, mails, log export, solicitations, create engagement).
- [ ] **3.3** Validazioni server-side: date (non passato, end ≥ start, ≤60 giorni), obbligatorietà scope (`Host`, `Contact`), credenziali (`Vpn`, `Users`, `DeliveryMethod`), email DL valida, almeno 1 destinatario `Cliente`, cliente non rimuove destinatari `Provider`.
- [ ] **3.4** Regole invarianti (§4.2 doc) nei service, non negli endpoint: pianificazione bloccata, prep solo dopo accepted, report visibili solo se `Approved && Published`, nuovo file resetta review, pending → superseded, resched accettata sostituisce la precedente, audit immutabile.
- [ ] **3.5** Audit: `AuditService` append-only, `All|ProviderOnly` + `ClientText`, eventi per ogni azione (upload, download, publish, mail, solleciti, export), filtro client.
- [ ] **3.6** `GET /api/dashboard/todo` + `GET /api/engagements/{id}` restituiscono `phase`, `stepIndex`, `todo` calcolati server-side.
- [ ] **3.7** Test di integrazione per ogni endpoint (happy path + 400/403 + cross-tenant).

**Accettazione:** tutti gli endpoint del §5 rispondono con DTO; invarianti coperte da test; nessuna regressione demo.
**Rischio deploy:** basso/medio (endpoint nuovi, vecchi mantenuti).

### Fase 4 — UI shell + lista engagement (3–4 giorni)

- [ ] **4.1** Sostituire `wwwroot/index.html` con shell (header **Cyber Engagement Portal**, user, role switch demo, data demo, main, toast, modal) e spostare CSS/JS in file separati.
- [ ] **4.2** Portare 1:1 la palette e i componenti CSS della demo (`:root`, `.card`, `.pill`, `.stepper`, `.tabs`, `.chipb`, `.sw`, `.modal`, `#toast`, responsive ≤820px), con titolo/logo del prodotto.
- [ ] **4.3** Stato frontend minimale (`state.js`): `role`, `tenant`, `demoDate`, `cur`, `tab`, `listTab`, `drafts` (persistenza input in `sessionStorage`).
- [ ] **4.4** Lista: KPI (attivi, azioni richieste, prossima attività, completati), tabella, mini-progress, pill di stato, periodo, filtro "azione richiesta".
- [ ] **4.5** Vista "Solleciti automatici" (sola lettura per client/auditor; editing per PMO) con regole, soglie, situazioni dovute, storico.
- [ ] **4.6** Accessibilità base: focus visibile, `aria-*` su tab/modali/toast, contrasti, navigazione da tastiera.

**Accettazione:** confronto visivo con `demo-poc.html` a parità di dati seed; nessun `JSON.stringify` in UI; branding corretto.
**Rischio deploy:** basso.

### Fase 5 — Dettaglio engagement: stepper e tab (5–7 giorni)

Per ciascuna scheda, replica esatta della funzione demo indicata.

- [ ] **5.1** Header dettaglio + **stepper 5 step** + pill stato + breadcrumb.
- [ ] **5.2** `tabOverview` — dettagli, ultime attività, note "prossima azione" (`nextAction`).
- [ ] **5.3** `tabDocs` — **manleva** (template + upload firmato), **documento di ingaggio**, **ordine** (modale riferimento), **business case**, credenziali (visibilità per ruolo), deliverables. Rispetto `VIS[role]`.
- [ ] **5.4** `tabPlan` — proposte, stati, accetta/rifiuta con motivazione, nuova proposta, riprogrammazione, blocco se `!canPlan`.
- [ ] **5.5** `tabScope` — vista/modifica, upload Swagger/OpenAPI, campi obbligatori, lock prima dell'accettazione.
- [ ] **5.6** `tabCred` — vista/modifica, metodi di consegna, avviso "niente password", lock per ruoli non autorizzati.
- [ ] **5.7** `tabReports` — gate 2FA demo, review card, upload bozze, download sicuro simulato, riepilogo findings, pubblicazione solo se approvato.
- [ ] **5.8** `tabDL` — tabella, dominio diverso, aggiunta/rimozione con regole cliente/Provider.
- [ ] **5.9** `tabMail` — 3 trigger, toggle, invio ora, storico; per il cliente solo "Email ricevute".
- [ ] **5.10** `tabLog` — filtro autore, export CSV, visibilità differenziata e `ClientText`.
- [ ] **5.11** Chip di navigazione (DL/Email/Registro) + legenda stati schede (`s-ok/s-todo/s-wait/s-lock`), `tabState`/`defaultTab` portati fedelmente.
- [ ] **5.12** Modali e toast come da demo; conferme distruttive.

**Accettazione:** per ognuno dei 5 ruoli e dei 9 engagement demo, ogni scheda mostra gli stessi contenuti/stati della demo (checklist manuale + snapshot test DOM opzionale), con naming ufficiale.
**Rischio deploy:** basso (feature flag `Features:NewUi` per switch rapido).

### Fase 6 — Workflow end-to-end e registro (2–3 giorni)

- [ ] **6.1** Flusso completo: creazione → manleva → ordine → proposta → accettazione → scope/credenziali → esecuzione → bozze → revisione manager → pubblicazione → email.
- [ ] **6.2** Riprogrammazione: sostituzione della proposta accettata, `replaced`, audit.
- [ ] **6.3** Reset review su nuovo file; blocco pubblicazione senza approvazione; cliente vede solo pubblicati.
- [ ] **6.4** Audit end-to-end: ogni transizione genera evento con visibilità corretta e `ClientText` per il cliente.
- [ ] **6.5** Test E2E API (uno per ruolo) + test Playwright (facoltativo ma consigliato) sul flusso principale.

**Accettazione:** scenari §4.2/§4.4 del doc verdi; nessuna azione permessa fuori matrice.

### Fase 7 — File, download sicuri, storage (3–4 giorni)

- [ ] **7.1** `IBlobStorage` con implementazioni `AzureBlobStorage` e `LocalDiskStorage` (demo).
- [ ] **7.2** Upload: limiti dimensione (es. 25 MB), allowlist MIME/estensioni (PDF/XLSX/DOCX/JSON/YAML), nome sicuro, virus-scan hook (Defender for Storage in fase 9).
- [ ] **7.3** Download: SAS/token 15 min monouso, watermark (testo utente+data per PDF), audit di ogni generazione/download.
- [ ] **7.4** Sostituire `FileRef` con riferimento storage; migrazione dei metadati demo.
- [ ] **7.5** Test: upload non autorizzato, MIME errato, token scaduto, download tracciato, cross-tenant.

**Accettazione:** §4.4 doc (SAS a scadenza breve e tracciati) verificato.

### Fase 8 — Email e solleciti automatici (3–4 giorni)

- [ ] **8.1** `IEmailSender` + `LoggingEmailSender` (demo) + provider reale dietro config.
- [ ] **8.2** Trigger email: inizio, fine, consegna deliverables; toggle `Auto`; invio manuale con precondizioni; storico `MailHistory`; visibilità interna/cliente.
- [ ] **8.3** Seed delle 6 `SolicitationRule` con soglie demo (3/5/2/7/2/1 giorni), chiave `manleva` per la prima regola.
- [ ] **8.4** `SolicitationService` con le 6 condizioni esatte della demo (`remDue`), idempotenza via `IdempotencyKey`.
- [ ] **8.5** `SolicitationWorker` (`IHostedService`) giornaliero + endpoint `GET /due` e `POST /{key}/send`; protezione da doppio invio in scale-out (lock DB).
- [ ] **8.6** Test: regole con date diverse, soglie, disattivazione, idempotenza, storico.

**Accettazione:** con la data demo avanzata, i solleciti/email compaiono come in demo; nessun duplicato.

### Fase 9 — Hardening sicurezza (3–4 giorni)

- [ ] **9.1** Key Vault per segreti e connection string (managed identity App Service).
- [ ] **9.2** RLS Azure SQL (`SESSION_CONTEXT('TenantId')`) + test cross-tenant a livello DB.
- [ ] **9.3** Security headers (CSP, HSTS, X-Content-Type-Options, Referrer-Policy), antiforgery/CSRF per endpoint cookie-based, rate limiting su auth/download.
- [ ] **9.4** Conditional Access/step-up sui report; rimozione del gate 2FA finto in produzione.
- [ ] **9.5** Allowlist tenant in produzione; onboarding con approvazione admin.
- [ ] **9.6** Pipeline: secret scanning, dependency audit, `dotnet list package --vulnerable`.

**Accettazione:** checklist sicurezza §2.6 doc soddisfatta; penetration self-check.

### Fase 10 — Test, osservabilità, documentazione (3–4 giorni)

- [ ] **10.1** Copertura unit ≥70% su FSM/Todo/regole; integration su tutti gli endpoint; E2E sul flusso felice.
- [ ] **10.2** Application Insights (o log App Service) + alert su 5xx, latenza, errori DB; `/health/ready`.
- [ ] **10.3** Documentazione: `docs/architecture.md`, `docs/api.md` (OpenAPI), `docs/azure-setup.md`, `docs/runbook.md` (deploy/rollback), `docs/naming.md` (glossario ufficiale), aggiornare `README.md`.
- [ ] **10.4** Aggiornare `documentazione.txt` con le decisioni prese (SPA, hosting, provider email) o creare `docs/decisions/*.md` (ADR).

### Fase 11 — Go-live (1–2 giorni)

- [ ] **11.1** SKU: F1 per demo, B1+ per produzione con Always On e slot staging.
- [ ] **11.2** Backup/restore DB testato; piano di rollback documentato.
- [ ] **11.3** Smoke test post-deploy automatici (health + 3 chiamate autenticate).
- [ ] **11.4** Verifica finale dei criteri di accettazione (§7).

---

## 7. Criteri di accettazione finali (dal §4.4 del documento)

1. Un utente di un tenant esterno accede via SSO e vede **solo** i propri engagement.
2. Ogni ruolo vede solo schede/azioni consentite (matrice completa testata).
3. Workflow end-to-end funzionante: creazione → manleva → ordine → pianificazione → prep → esecuzione → report → revisione → pubblicazione.
4. Download protetti da SAS a scadenza breve e tracciati.
5. Audit log fedele, immutabile, con visibilità differenziata e `ClientText`.
6. Demo pubblicata su App Service e accessibile via SSO multitenant.
7. Parità UX con `demo-poc.html`: stepper, tab con stato, chip, legenda, modali, toast, draft.
8. Deploy su `main` sempre verde in ~1 minuto, con verifica automatica.
9. Branding **Cyber Engagement Portal** e naming ufficiale ovunque (nessun nome fornitore, nessuna sigla storica).

---

## 8. Matrice di tracciabilità demo → implementazione

| Funzione demo | Dove va | Fase |
|---|---|---|
| `ROLES`, `isProvider/canWrite/seesInt/canCreds/canMail/canRem/canDecide` | `Auth/AuthorizationPolicies.cs` + `UserContext` | 2 |
| `phase`, `statusOf`, `STEP_OF`, `STEPS` | `Services/EngagementWorkflow.cs` + DTO | 1 |
| `myTodo`, `nextAction` | `Services/TodoResolver.cs` + `tabOverview` | 1–5 |
| `tabDocs`, `tabPlan`, `tabScope`, `tabCred`, `tabReports` | `wwwroot/js/views/*` + endpoint dedicati | 5 |
| `tabDL`, `tabMail`, `tabLog` | `wwwroot/js/views/*` + endpoint | 3–5 |
| regole solleciti, `remDue`, `remSend`, `tickRem` | `SolicitationService/Worker` + UI | 8 |
| metadati email, `tickMail`, `doSend` | `MailService` + UI | 8 |
| download protetto, gate 2FA | `Storage` SAS + step-up | 2/7 |
| `exportLog` | `GET /log/export` (CSV) | 3 |
| `openModal`, `toast`, `saveDrafts/restoreDrafts` | componenti JS | 4–5 |
| dataset demo (`E`, `DLC`, DL interna) | `DemoDataSeeder` con naming ufficiale | 1 |

---

## 9. Rischi e mitigazioni

| Rischio | Probabilità | Impatto | Mitigazione |
|---|---|---|---|
| La CI si rompe quando si attiva l'auth (verifica su endpoint protetto) | Alta | Deploy rosso | Step 0.3 sposta la verifica su `/health` **prima** di ogni auth |
| `Results.Forbid()` senza handler auth → 500 | Alta | Errori runtime | Step 0.5 + test |
| Migrazione EF fallisce all'avvio su App Service | Media | App down | Retry + feature flag + fallback in-memory + migrazione testata in staging |
| Quota CPU F1 esaurita / cold start | Media | Demo lenta | Dati leggeri, no job pesanti, apertura anticipata, upgrade temporaneo B1 |
| Perdita dati in-memory tra deploy | Certa (oggi) | Demo incoerente | EF + seed idempotente (Fase 1) |
| Divergenza FSM demo vs codice | Alta (già presente) | Stato errato in UI | Test-first dei 6 casi (Fase 0.7/1.8) |
| Scope creep (Blazor, Angular, microservizi) | Media | Ritardo | Default SPA statica; ADR e decisione esplicita |
| Cross-tenant leak | Bassa | Critico | Query filter + test integration + RLS (Fase 9) |
| Segreti nel repo | Bassa | Critico | Key Vault + secret scanning |
| Terminologia non conforme o nome fornitore residuo | Media | Problema legale/branding | `NamingGuardTests` bloccante in CI (step 0.9) |

---

## 10. Ordine consigliato dei primi 10 push

1. `/health` + workflow check + ProblemDetails + enum string (Fase 0).
2. Rinominazione/branding **Cyber Engagement Portal** + naming guard (step 0.9).
3. Test FSM/TodoResolver sul comportamento attuale (rete di sicurezza).
4. Nuovo dominio completo + `PortalDbContext` + prima migrazione (dietro flag).
5. `EfEngagementRepository` + seeder demo + fallback in-memory.
6. Endpoint read-only su EF (lista/dettaglio/dashboard) + test integrazione.
7. Parser claims + policy + DemoMode (senza enforcement in produzione).
8. Endpoint preparazione (scope/credenziali/business case) + validazioni.
9. Endpoint comunicazioni (DL/mail/log export) + audit service.
10. Endpoint report/download/review/publish + storage locale.

Ogni push: `dotnet build -c Release`, `dotnet test`, attesa verde del workflow, smoke test manuale su `/health` e su 2–3 flussi.

---

## 11. Stima complessiva

| Fase | Giorni (1 dev) |
|---|---|
| 0 — Guardrail + branding | 1–2 |
| 1 — Dominio + EF | 3–5 |
| 2 — Auth | 2–4 |
| 3 — API | 3–5 |
| 4 — UI shell/lista | 3–4 |
| 5 — Dettaglio/tab | 5–7 |
| 6 — Workflow E2E | 2–3 |
| 7 — File/SAS | 3–4 |
| 8 — Email/solleciti | 3–4 |
| 9 — Hardening | 3–4 |
| 10 — Test/doc | 3–4 |
| 11 — Go-live | 1–2 |
| **Totale** | **~32–48 giorni lavorativi** |

Percorso minimo per una demo convincente e sicura (fasi 0–6, con seed e gate 2FA demo): **~19–27 giorni**.

---

## 12. Decisioni aperte (da confermare prima della Fase 1)

1. **Frontend:** SPA statica (default) o Blazor? Se Blazor, quale modello (Server vs WASM)?
2. **Database:** Azure SQL free tier/serverless (default) o SQLite persistente su App Service (più economico, meno fedele al doc)?
3. **Email:** Microsoft Graph (default) o SendGrid?
4. **Storage:** Azure Blob (default) o filesystem locale per la demo?
5. **SKU:** restare su F1 per la demo o passare a B1 per Always On?
6. **Dati demo:** mantenere Nova Bank/Orion Energia come in `demo-poc.html` o usare i 3 seed attuali?
7. **Admin onboarding tenant:** automatico (demo) o con approvazione (produzione)?
8. **2FA:** restare con gate demo dietro flag o rimuoverlo del tutto in favore di Conditional Access?

---

*Piano redatto il 14/11/2026. Riferimenti: `demo-poc.html`, `documentazione.txt`, `Program.cs`, `Domain/Models.cs`, `Services/EngagementWorkflow.cs`, `Services/InMemoryEngagementRepository.cs`, `wwwroot/index.html`, `.github/workflows/main_cyber-engagement-demo.yml`.*
