# Cyber Engagement Portal

Portale per la gestione degli engagement di cybersecurity: clienti e utenze,
workflow in sei passi (ordine, manleva e costi; scope; pianificazione;
credenziali; esecuzione; report), revisione, distribuzione ed email.

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
- **Team**: partecipanti esterni per cliente (base comune) e per engagement.
  Gli inviti sono aperti a tutti i profili tranne l'Auditor; rimuovono il
  Manager (tutti) e il Cliente (solo organizzazione `Cliente`).
- **Documenti**: scheda tecnica con manleva, documento di ingaggio, ordine,
  **costi e margine** dell'attività (margine in percentuale sul costo).
- **Scope**: perimetro tecnico, obiettivi e finestre dell'attività.
- **Pianificazione**: proposte di date, accettazione/rifiuto, richiesta di
  riprogrammazione, durata massima 60 giorni. Per Cliente e PMO, se sono passati
  meno di **14 giorni dalla richiesta**, il tab Pianificazione mostra un
  **avviso non bloccante** con la data da cui proporre; l'invio resta possibile
  dopo una conferma.
- **Credenziali**: dati di accesso per l'esecuzione (consegna tramite
  vault/canale separato, nessuna password conservata nel portale).
- **Esecuzione e report**: attività in corso, bozze caricate dal Pentester,
  revisione del Manager, pubblicazione al cliente, download tracciato con
  **placeholder 2FA** per il Cliente. Cliente, PMO e Manager possono generare
  **link monouso** validi **5 giorni** e consumati al primo download. Il
  Cliente vede solo i deliverable pubblicati.
- **Ritardo preparazione**: la lista segnala gli engagement con scope o
  credenziali mancanti a due settimane dall'avvio dell'esecuzione.
- **Comunicazioni**: **due distribution list** per engagement, ciascuna con una
  **base comune del cliente**: *notifiche* (inizio/fine attività, solleciti) e
  *deliverables* (invio dei report). Email automatiche di inizio, fine e
  consegna deliverables (per ora simulate nel registro, senza invio SMTP reale);
  **template** standard, per cliente e per engagement, con anteprima e
  placeholder.
- **Solleciti**: regole con soglia in giorni, elenco dei solleciti dovuti e
  invio, gestiti dal PMO.
- **Registro audit**: visibilità differenziata per profilo, esportazione CSV.
- **Dashboard**: endpoint `/api/dashboard/todo` con la prossima azione per ogni
  engagement e profilo.
- **Homepage**: quattro tessere che sono anche filtri (da gestire, in attesa di
  altri, a rischio avvio, prossimo avvio), colonna **Da gestire ora** con CTA
  diretta, attese in evidenza e tabella compatta con la **fase del workflow**
  (barra a 6 passi + nome), ricerca su tutti i campi, filtro azzerabile con
  **× Azzera filtri** e **colonne ordinabili** (click sull'intestazione, con
  indicatore di direzione).
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
| Caricare documento di ingaggio e ordine | I | A/R | I | I | I |
| Inserire costi e margine dell'attività | — | R | I | A/R | I |
| Approvare costi e margine dell'attività | — | I | I | A/R | I |
| Proporre date o richiedere riprogrammazione | R | A/R | I | I | I |
| Approvare o rifiutare una proposta di date | I | I | I | A/R | I |
| Compilare scope e credenziali | R | A/R | I | R (solo credenziali) | — |
| Gestire template email standard e per cliente | — | — | I (sola lettura) | A/R | — |
| Gestire template email dell'engagement | — | — | A/R | A/R | — |
| Gestire le distribution list base del cliente (notifiche e deliverables) | I | I | I | A/R | — |
| Invitare partecipanti esterni (cliente o engagement) | R | R | R | R | — |
| Rimuovere partecipanti esterni | R (solo organizzazione Cliente) | — | — | A/R (tutti) | — |
| Caricare report | I | I | A/R | I | I |
| Inviare report in revisione al Manager | I | I | A/R | I | I |
| Approvare o richiedere modifiche ai report | I | I | C | A/R | I |
| Pubblicare i report al cliente | I | I | A/R (dopo approvazione) | C | I |
| Scaricare report (il Cliente solo i pubblicati) | R | R | R | R | R |
| Gestire le distribution list (notifiche e deliverables) | R (solo destinatari Cliente) | A/R (tutti) | I | I | I |
| Configurare e inviare le email automatiche | I | A/R | I | I | I |
| Configurare e inviare i solleciti | — | A/R | I | I | I |
| Consultare il registro audit | R (eventi visibili) | R | R | R | R |
| Esportare il registro audit in CSV | R | R | R | R | R |

### Cosa può fare ogni ruolo

- **Cliente**: aggiunge informazioni ai propri engagement (manleva, scope,
  credenziali, destinatari Cliente della distribution list), propone le date,
  scarica la reportistica pubblicata. Invita partecipanti esterni al team e
  rimuove quelli della propria organizzazione (`Cliente`). Non vede i dati degli
  altri clienti.
- **PMO**: coordina gli engagement dei clienti; crea engagement, carica documento
  di ingaggio/ordine e manleva per conto del cliente, inserisce costi e margine,
  gestisce pianificazione, scope, credenziali, distribution list
  dell'engagement, email automatiche e solleciti. Inserisce costi e margine,
  che devono essere approvati dal Manager. Invita partecipanti esterni al team
  ma non li rimuove. Non vede i template email.
- **Pentester**: consulta gli engagement e le informazioni, carica i report, li
  invia in revisione al Manager e li pubblica al cliente dopo l'approvazione.
  Vede e personalizza il template email dell'engagement; invita partecipanti
  esterni al team ma non li rimuove; non crea engagement e non gestisce email o
  solleciti.
- **Manager**: crea e gestisce clienti e utenze, crea engagement, inserisce e
  approva costi e margine, rivede e approva i report del Pentester. Vede e
  modifica le credenziali; gestisce i template email standard e per cliente (e
  quelli per engagement insieme al Pentester); invita e rimuove qualsiasi
  partecipante esterno del team. Non gestisce scope, planning, email o
  solleciti.
- **Auditor**: vede tutto in sola lettura, tranne credenziali e template email;
  non invita partecipanti al team.

### Allineamento con il codice

- Creazione engagement: `POST /api/engagements` consente PMO e Manager.
- Clienti e utenze: `POST/PUT /api/clients...` riservati al Manager; `GET
  /api/clients` filtra il Cliente sul proprio cliente.
- Manleva: `POST /api/engagements/{id}/manleva` consente Cliente e PMO.
- Documento di ingaggio e ordine: solo PMO.
- Costi e margine: `PUT /api/engagements/{id}/financials` consente PMO e Manager
  nella prima fase. Il margine è una **percentuale tra 0 e 100** (default nel form
  **46%**); il valore dell'attività mostrato in UI è costo + margine%. Se i dati
  li inserisce il PMO restano **in attesa di approvazione**
  (`POST /financials/approve`, solo Manager); se li inserisce il Manager sono
  approvati contestualmente. Il primo passo si completa con manleva, ordine,
  costi e margine **approvati**.
- Proposte: `POST /api/engagements/{id}/proposals` consente Cliente e PMO; la
  decisione spetta solo al Manager (`decide` restituisce 403 agli altri ruoli).
  Se sono passati meno di 14 giorni da `CreatedDate`, il dettaglio espone
  `planningNotice` a Cliente e PMO: è un avviso, non un blocco.
- Scope: `CanEditPreparation` consente Cliente e PMO; è modificabile appena
  manleva e ordine sono completi, in tutte le fasi successive (Scope,
  Pianificazione, Credenziali, Scheduled; anche Running per il PMO).
- Credenziali: `CanEditCredentials` consente Cliente e PMO come sopra e il
  Manager fino alla fase di Reporting (non a engagement concluso). Visibili a
  Client, PMO, Pentester e Manager; non all'Auditor.
- Template email: `GET/PUT /api/engagements/{id}/mail-templates` è riservato a
  Manager e Pentester. Il Manager modifica standard, cliente ed engagement; il
  Pentester solo l'engagement. Cliente, PMO e Auditor ricevono 403 e non vedono
  la sezione.
- Distribution list: due liste separate (notifiche e deliverables), ognuna con
  una base comune per cliente gestita dal Manager
  (`/api/clients/{id}/distribution-list?kind=notifications|deliverables`);
  l'engagement può aggiungere destinatari propri. La lista effettiva è base +
  engagement, deduplicata per email. Se la lista deliverables è vuota si usano
  i destinatari delle notifiche come fallback.
- Team: partecipanti esterni in `Engagement.Team` (specifici) e
  `ClientAccount.Team` (base cliente); il team effettivo è base + engagement,
  deduplicato. Inviti a tutti tranne l'Auditor; rimozione a Manager (tutti) e
  Cliente (solo organizzazione `Cliente`).
- Email: il servizio `MailAutomationService` prepara le voci di registro per
  inizio e fine attività (lista **notifiche**) e consegna deliverables (lista
  **deliverables**) quando le condizioni sono soddisfatte; non contatta alcun
  server SMTP. L'invio manuale resta riservato al PMO.
- Report: caricamento, invio in revisione e pubblicazione solo Pentester;
  approvazione solo Manager; pubblicazione subordinata a `ReviewStatus.Approved`.
  Il download diretto del Cliente passa da una modale **2FA demo** (placeholder,
  qualsiasi codice di 6 cifre). I link monouso sono creabili da Cliente, PMO e
  Manager solo su report pubblicati: scadono dopo 5 giorni e si invalidano al
  primo uso.
- Distribution list dell'engagement: scrittura Cliente/PMO; il Cliente può
  gestire solo i destinatari con organizzazione `Cliente`. La base comune del
  cliente è gestita dal Manager.
- Email: toggle e invio solo PMO.
- Solleciti: lettura per i profili interni, scrittura e invio solo PMO; il
  Cliente non ha accesso alle regole.

## Dati e persistenza

L'applicazione ha due modalità:

- **Azure SQL** (consigliata in produzione): attiva quando è configurata la
  connection string `ConnectionStrings:EngagementPortal` (o la variabile
  `SQLCONNSTR_EngagementPortal` di Azure App Service). I repository
  `SqlEngagementRepository`, `SqlClientRepository` e
  `SqlAppSettingsRepository` usano EF Core con
  `Microsoft.EntityFrameworkCore.SqlServer`. I template standard stanno nella
  tabella `Settings`, creata automaticamente anche su database esistenti.
- **In memoria** (fallback locale/demo): se non c'è connection string, usa
  `InMemoryEngagementRepository`, `InMemoryClientRepository` e
  `InMemoryAppSettingsRepository`. I dati si perdono al riavvio, compresi gli
  override dei template standard.

> Se la connection string è presente ma il database non è raggiungibile, l'app
> termina all'avvio (fail-fast): meglio un errore evidente che un fallback
> silenzioso su dati volatili.

> I costi e il margine (percentuale) sono obbligatori e devono essere
> **approvati** per chiudere la prima fase: gli engagement esistenti privi di
> questi valori o dell'approvazione restano in fase **Docs** finché PMO o Manager
> non li inseriscono e il Manager non li approva.

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

### Dataset demo

Il seed (`EngagementPortal.Web/Services/SeedData.cs`) crea **15 engagement** su
**6 clienti** (Contoso, Fabrikam, Nova Bank, Orion Energia, Tailspin Toys,
Northwind Traders) e copre tutti i passi del workflow:

| Caso | Engagement | Cosa mostra |
| --- | --- | --- |
| Primo passo da completare | ENG-2026-002 | manleva, ordine e costi mancanti |
| Costi in attesa di approvazione | ENG-2026-012 | PMO ha inserito i dati, serve il Manager |
| Scope | ENG-2026-001 / 007 | proposta di date in attesa / scope e credenziali mancanti a 9 giorni dall'avvio |
| Pianificazione | ENG-2026-009 | scope pronto, nessuna proposta di date |
| Credenziali | ENG-2026-008 | accessi non ancora forniti dal cliente |
| Pianificato | ENG-2026-005 / 010 / 014 | avvio futuro, anche su rete OT e SAP |
| In corso | ENG-2026-004 / 013 | red team |
| Reporting | ENG-2026-003 / 011 | revisione Manager in attesa / modifiche richieste |
| Concluso | ENG-2026-006 / 015 | deliverable pubblicato al cliente (015 è di Contoso, così il profilo Cliente vede un report scaricabile) |

Costi e margine (margine al **46%**) sono valorizzati e approvati per tutti gli
engagement tranne quelli usati per mostrare il primo passo e l'attesa di
approvazione. Target
(`*.example`, `10.x`), credenziali e riferimenti ai segreti (`kv://demo/...`)
sono inventati.

I documenti demo sono file di testo in
`EngagementPortal.Web/wwwroot/samples/documents/`: manleva, documento di
ingaggio e report contengono il nome del documento e dell'engagement, e si
aprono dal tab **Documenti** con il pulsante **Apri**.

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
engagement, gestione clienti e tutte le schede operative.
Il dettaglio usa lo **split editor C3**: workflow a sinistra come navigazione,
pannello operativo al centro con **editing inline** (niente modali “Modifica”)
e colonna di contesto a destra con prossimo passo, persone, date ed economia.
La lista **Persone** è interattiva: click su una persona per aprire il team,
`+` per invitare e `×` per rimuovere (secondo i permessi). Costi e margine sono
visibili solo a PMO e Manager e restano **compressi di default** nel dettaglio.
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
| POST | `/api/engagements/{id}/manleva` · `/documento-ingaggio` · `/order` |
| PUT | `/api/engagements/{id}/financials` |
| POST | `/api/engagements/{id}/financials/approve` |
| GET/PUT | `/api/engagements/{id}/mail-templates` |
| POST/DELETE | `/api/clients/{id}/distribution-list` · `/{entryId}` (`?kind=notifications\|deliverables`) |
| POST/DELETE | `/api/engagements/{id}/team` · `/{memberId}` |
| POST/DELETE | `/api/clients/{id}/team` · `/{memberId}` |
| GET/POST | `/api/engagements/{id}/proposals` |
| POST | `/api/engagements/{id}/proposals/{pid}/decide` |
| GET/PUT | `/api/engagements/{id}/scope` |
| GET/PUT | `/api/engagements/{id}/credentials` |
| GET/POST | `/api/engagements/{id}/reports` |
| POST | `/api/engagements/{id}/reports/{rid}/download` |
| POST | `/api/engagements/{id}/reports/{rid}/links` (link monouso, 5 giorni) |
| GET | `/api/downloads/{token}` (pubblico, monouso) |
| POST | `/api/engagements/{id}/review/submit` · `/review/decide` · `/reports/publish` |
| GET/POST/DELETE | `/api/engagements/{id}/distribution-list` (`?kind=notifications\|deliverables`) |
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
- `EngagementPortal.Web/wwwroot/engagement-ux-proposals.html` — tre proposte
  navigabili per il layout del dettaglio engagement (inline editing e workflow)
- `EngagementPortal.Web/wwwroot/engagement-ux-split-variants.html` — split editor
  in 4 varianti, con profilo demo e visibilità costi solo per PMO/Manager
