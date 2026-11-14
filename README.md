# Cyber Engagement Portal

Portale per la gestione degli engagement di cybersecurity: clienti e utenze,
workflow in sei passi (ordine e manleva, scope, pianificazione, credenziali,
esecuzione, report), revisione, distribuzione ed email.

Applicazione ASP.NET Core (`EngagementPortal.Web`) con deploy automatico su Azure
App Service a ogni push su `main`.

## Funzionalità

- **Ruoli e clienti**: Cliente, PMO, Pentester, Manager, Auditor. Il profilo
  Cliente vede solo gli engagement del proprio cliente; i profili interni (PMO,
  Pentester, Manager, Auditor) vedono tutti i clienti e possono filtrare per
  cliente dal selettore in alto.
- **Clienti e utenze**: il Manager crea e modifica i clienti (ragione sociale e
  sito web), associa o rimuove utenze e vede gli engagement collegati. La
  favicon del sito diventa l'icona del cliente; senza sito si usano le iniziali.
  Gli altri profili interni possono consultare l'anagrafica.
- **Documenti**: scheda tecnica con manleva, documento di ingaggio, ordine,
  business case.
- **Scope**: perimetro tecnico, obiettivi e finestre dell'attività.
- **Pianificazione**: proposte di date, accettazione/rifiuto, richiesta di
  riprogrammazione, durata massima 60 giorni.
- **Credenziali**: dati di accesso per l'esecuzione (consegna tramite
  vault/canale separato, nessuna password conservata nel portale).
- **Esecuzione e report**: attività in corso, bozze caricate dal Pentester,
  revisione del Manager, pubblicazione al cliente, download tracciato. Il
  Cliente vede solo i deliverable pubblicati.
- **Ritardo preparazione**: la lista segnala gli engagement con scope o
  credenziali mancanti a due settimane dall'avvio dell'esecuzione.
- **Comunicazioni**: distribution list e email automatiche di inizio, fine e
  consegna deliverables, gestite dal PMO.
- **Solleciti**: regole con soglia in giorni, elenco dei solleciti dovuti e
  invio, gestiti dal PMO.
- **Registro audit**: visibilità differenziata per profilo, esportazione CSV.
- **Dashboard**: endpoint `/api/dashboard/todo` con la prossima azione per ogni
  engagement e profilo.
- **Homepage**: quattro tessere che sono anche filtri (da gestire, in attesa di
  altri, a rischio avvio, prossimo avvio), colonna **Da gestire ora** con CTA
  diretta, attese in evidenza e tabella compatta con la **fase del workflow**
  (barra a 6 passi + nome), ricerca su tutti i campi, filtro e ordinamento.
- **Responsive**: layout da 320 px ai monitor ultra-wide. Il contenuto cresce
  fino a 1680 px; la tabella engagement diventa a schede sotto 820 px, l'header
  si compatta sotto 760 px e le card dei clienti passano a 2–3 colonne sui
  monitor larghi.

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
| Accesso in lettura agli engagement | R (proprio cliente) | R | R | A/R | R (sola lettura) |
| Creare clienti | I | I | I | A/R | I |
| Rinominare clienti | I | I | I | A/R | I |
| Aggiungere/rimuovere utenze cliente | I | I | I | A/R | I |
| Consultare clienti, utenze ed engagement associati | R (solo proprio) | R | R | A/R | R |
| Creare engagement | I | R | I | A/R | I |
| Caricare scheda tecnica / manleva | A/R | R (per conto del cliente) | I | I | I |
| Caricare documento di ingaggio, ordine e business case | I | A/R | I | I | I |
| Proporre date o richiedere riprogrammazione | R | A/R | I | I | I |
| Approvare o rifiutare una proposta di date | I | I | I | A/R | I |
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
  credenziali, destinatari Cliente della distribution list), propone le date,
  scarica la reportistica pubblicata. Non vede i dati degli altri clienti.
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
  /api/clients` filtra il Cliente sul proprio cliente.
- Manleva: `POST /api/engagements/{id}/manleva` consente Cliente e PMO.
- Documento di ingaggio, ordine e business case: solo PMO.
- Proposte: `POST /api/engagements/{id}/proposals` consente Cliente e PMO; la
  decisione spetta solo al Manager (`decide` restituisce 403 agli altri ruoli).
- Scope e credenziali: `CanEditPreparation` consente Cliente e PMO; sono
  modificabili appena manleva e ordine sono completi, in tutte le fasi
  successive (Scope, Pianificazione, Credenziali, Scheduled; anche Running per
  il PMO). Le credenziali non sono visibili a Manager e Auditor.
- Report: caricamento, invio in revisione e pubblicazione solo Pentester;
  approvazione solo Manager; pubblicazione subordinata a `ReviewStatus.Approved`.
- Distribution list: scrittura Cliente/PMO; il Cliente può gestire solo i
  destinatari con organizzazione `Cliente`.
- Email: toggle e invio solo PMO.
- Solleciti: lettura per i profili interni, scrittura e invio solo PMO; il
  Cliente non ha accesso alle regole.

## Dati e persistenza

L'applicazione ha due modalità:

- **Azure SQL** (consigliata in produzione): attiva quando è configurata la
  connection string `ConnectionStrings:EngagementPortal` (o la variabile
  `SQLCONNSTR_EngagementPortal` di Azure App Service). I repository
  `SqlEngagementRepository` e `SqlClientRepository` usano EF Core con
  `Microsoft.EntityFrameworkCore.SqlServer`.
- **In memoria** (fallback locale/demo): se non c'è connection string, usa
  `InMemoryEngagementRepository` e `InMemoryClientRepository`. I dati si
  perdono al riavvio.

> Se la connection string è presente ma il database non è raggiungibile, l'app
> termina all'avvio (fail-fast): meglio un errore evidente che un fallback
> silenzioso su dati volatili.

Con Azure SQL vengono create due tabelle:

| Tabella | Chiave | Contenuto |
| --- | --- | --- |
| `Engagements` | `Id` | `ClientId`, `Payload` JSON dell'aggregato, `UpdatedAt` |
| `Clients` | `Id` | `Name`, `Payload` JSON con le utenze, `UpdatedAt` |

L'aggregato viene salvato come JSON per mantenere semplice il dominio: una riga
per engagement e una per cliente. Al primo avvio `DatabaseBootstrap` crea lo
schema con `EnsureCreated` e inserisce il seed demo solo se le tabelle sono
vuote. Non ci sono migrazioni automatiche: se in futuro cambia lo schema delle
tabelle, va aggiunta una migration EF Core.

> Cambio di schema recente: `Clients.Id` e `Engagements.ClientId` sostituiscono
> le colonne precedenti. `EnsureCreated` non aggiorna un database già
> esistente: in demo va ricreato, in ambienti con dati va applicato un `ALTER`
> esplicito prima del deploy.

> Limite noto: la scrittura è last-write-wins sull'intero aggregato. Per un uso
> concorrente intenso conviene normalizzare le tabelle o aggiungere un token di
> concorrenza (`rowversion`).

### Configurazione su Azure App Service

1. Creare un Azure SQL Database (esempio con Azure CLI):

   ```bash
   az group create --name rg-engagement --location westeurope
   az sql server create \
     --name <sql-server> --resource-group rg-engagement \
     --location westeurope \
     --admin-user portaladmin --admin-password '<password-forte>'
   az sql db create \
     --resource-group rg-engagement --server <sql-server> \
     --name engagementportal --service-objective Basic
   az sql server firewall-rule create \
     --resource-group rg-engagement --server <sql-server> \
     --name AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
   ```

   La regola `0.0.0.0` abilita i servizi Azure. Per la produzione è preferibile
   un private endpoint o la managed identity con un utente contenuto nel DB.

2. Impostare la connection string come **Application setting** dell'App Service
   (nome con doppio underscore):

   ```bash
   az webapp config appsettings set \
     --resource-group <rg-app> --name cyber-engagement-demo \
     --settings ConnectionStrings__EngagementPortal="Server=tcp:<sql-server>.database.windows.net,1433;Initial Catalog=engagementportal;Persist Security Info=False;User ID=portaladmin;Password=<password-forte>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
   ```

   La password non va mai committata: `appsettings.json` contiene solo una
   stringa vuota.

3. Riavviare l'App Service. Al primo avvio vengono creati schema e dati demo.
   `GET /health` e `GET /health/ready` riportano `storage: "azure-sql"`.

In locale, senza connection string, l'app parte comunque in memoria:

```bash
dotnet run --project EngagementPortal.Web
```

Per provare la persistenza in locale basta esportare la variabile
`ConnectionStrings__EngagementPortal` puntando a un SQL Server/Azure SQL
raggiungibile.

## Interfaccia

Aprendo l'applicazione (`/`) si usa la SPA completa: dashboard, dettaglio
engagement, stepper del workflow, gestione clienti e tutte le schede operative.
Il selettore profilo/cliente in alto permette di provare i diversi profili (demo).

> Nota: l'identità è simulata tramite parametri `role`/`client` per rendere la
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
| PUT | `/api/clients/{clientId}` |
| POST/DELETE | `/api/clients/{clientId}/users` · `/users/{userId}` |
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
