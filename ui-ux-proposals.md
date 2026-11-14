# Proposte UI/UX — Engagement Portal

Analisi dell'interfaccia attuale (`EngagementPortal.Web/wwwroot/index.html`) e proposte
di miglioramento. Le proposte sono numerate: puoi approvarne solo alcune.

---

## 1. Analisi dello stato attuale

### Cosa funziona già

- Palette centralizzata in variabili CSS (`:root`), quindi il restyling è a basso rischio.
- Esiste già una distinzione di stato nelle schede: completato / azione richiesta /
  in attesa / bloccato (`.tab.s-ok`, `.s-todo`, `.s-wait`, `.s-lock`).
- Esiste un `TodoResolver` lato server che sa, per ruolo, qual è la prossima azione.
- Stepper, pill, note e KPI sono componenti riutilizzabili e coerenti.

### Problemi principali

| # | Problema | Effetto |
|---|---|---|
| P1 | **Semantica dei colori incoerente**: il giallo (`--warn`) è usato sia per "in attesa di altri" sia per "azione richiesta"; il blu (`--info`) per "proposta da valutare"; il teal (`--prog`) per il reporting | L'utente non può imparare una regola unica: lo stesso colore significa cose diverse in punti diversi |
| P2 | **"azione richiesta" è gialla**, mentre la richiesta è rosso = bloccato da te | Il segnale più urgente è il meno evidente |
| P3 | **"In attesa di altri" è blu**, colore che altrove indica informazione/neutro | Attesa e informazione si confondono |
| P4 | **Lo stato cambia colore tra lista, dettaglio, tab e KPI** (es. proposta pending: blu in lista, teal nel dettaglio) | Incoerenza percepita come bug |
| P5 | **Logica di stato duplicata** in `listStatus`, `detailStatus`, `tabState`, `nextAction` | Le quattro copie divergeranno: correzioni fatte in un punto e non negli altri |
| P6 | **Manca il proprietario dell'azione**: "in attesa documenti" non dice *di chi* | L'utente non sa se deve agire lui o aspettare |
| P7 | **Rosso usato anche per errori/rifiuti** (`p-err`) con la stessa resa del "da fare" | Due significati diversi per lo stesso colore |
| P8 | **Colore unico veicolo di informazione** in alcuni punti | Problema di accessibilità (daltonismo) e stampa |
| P9 | **Nessun filtro/ordinamento per urgenza** nella lista | Con molti engagement le righe rosse si perdono |
| P10 | **Layout denso nel dettaglio**: stepper + 2 card + 5 tab + 3 chip + legenda | Rumore visivo, especially su mobile |
| P11 | **Stati di caricamento/errore minimi** ("Caricamento…" testuale), niente blocco dei bottoni durante le richieste | Doppio invio e feedback ambiguo |
| P12 | **Chrome demo** (ruolo/tenant) mescolata all'header prodotto | Rischio di scambiare la demo per un'app reale |

---

## 2. Principio proposto: un colore = un proprietario dell'azione

Regola unica, valida in tutta l'app:

> **Rosso** = la palla è a te: solo tu puoi sbloccare l'engagement.
> **Giallo** = la palla è a qualcun altro: stai aspettando cliente, PMO, Pentester o Manager.
> **Blu** = in corso: nessuno è bloccato, il tempo scorre.
> **Verde** = completato.
> **Grigio** = bloccato da un prerequisito o da un permesso (non è "colpa" di nessuno).

| Stato semantico | Significato | Token | Colore testo | Sfondo | Icona | Esempi |
|---|---|---|---|---|---|---|
| **Da fare (tuo)** | Azione richiesta a te, l'avanzamento è fermo qui | `--st-action` | `#B42318` | `#FEE4E2` | `!` | Firma manleva, registra ordine, accetta/rifiuta proposta, compila scope/credenziali, invia in revisione, pubblica, approva report |
| **In attesa (altri)** | A carico di cliente/PMO/Pentester/Manager | `--st-wait` | `#7A5B00` | `#FFF8C5` | `…` | In attesa firma cliente, ordine PMO, decisione sull'altra parte, revisione Manager |
| **In corso** | Nessuno bloccato | `--st-progress` | `#175CD3` | `#D1E9FF` | `▶` | Pianificato, in esecuzione, report in redazione |
| **Completato** | Chiuso positivamente | `--st-done` | `#067647` | `#DCFAE6` | `✓` | Firmato, accettata, approvata, pubblicata, done |
| **Bloccato** | Prerequisito mancante / permesso / sola lettura | `--st-blocked` | `#475467` | `#F2F4F7` | `🔒` | Pianificazione prima dei documenti, credenziali non visibili all'Auditor |
| **Neutro** | Bozza, superata, non inviata | `--st-neutral` | `#475467` | `#F2F4F7` | – | Bozza report, proposta sostituita |

### Regole di composizione

1. **Un solo stato primario per riga**: se ci sono sia un'azione tua sia un'attesa
   altrui, la riga è **rossa** e mostra un chip secondario giallo
   ("+ in attesa cliente").
2. **Il rosso è un accento, non uno sfondo**: pill, bordo sinistro della card,
   pallino, icona. Mai l'intera riga rossa: dopo 10 righe diventa illeggibile.
3. **Rosso "azione" ≠ rosso "errore"**: il rosso azione ha icona `!` e testo
   "Da fare"; il rosso errore compare solo in un messaggio con testo "Errore" e
   non come stato persistente di una riga.
4. **Mai solo colore**: sempre icona + parola (accessibilità WCAG 1.4.1).
5. **Contrasto ≥ 4.5:1** per il testo nelle pill (i valori proposti lo rispettano).

---

## 3. Proposte

### P1 — Design token semantici (priorità alta, effort S)

Sostituire l'uso diretto di `--warn`, `--err`, `--brand-*` per gli stati con token
che descrivono il significato, non il colore:

```css
:root{
  /* stato */
  --st-action:#B42318;  --st-action-bg:#FEE4E2;
  --st-wait:#7A5B00;    --st-wait-bg:#FFF8C5;
  --st-progress:#175CD3;--st-progress-bg:#D1E9FF;
  --st-done:#067647;    --st-done-bg:#DCFAE6;
  --st-blocked:#475467; --st-blocked-bg:#F2F4F7;
  --st-neutral:#475467; --st-neutral-bg:#F2F4F7;

  /* brand invariato: navy + cyan per link, bottoni, focus */
  /* danger invariato per azioni distruttive (Rifiuta, Elimina) */
}
```

Le classi diventano `.pill.st-action`, `.pill.st-wait`, ecc. Il brand blu resta
per l'interazione (bottoni primari, focus, link), non per gli stati.

**Impatto**: nessuna modifica alle API; solo CSS + i punti che scelgono la classe.

---

### P2 — Lista: stato con proprietario, KPI e legenda (priorità alta, effort S/M)

**Riga della lista**

Oggi: `In attesa documenti` (giallo) + `azione richiesta` (giallo).
Proposto:

```
Contoso – External PT          [● Da fare: firma manleva]    1–5 giu
                              [● In attesa: ordine PMO]
```

- Pill primaria rossa se `todo` riguarda il ruolo corrente.
- Seconda pill gialla con il nome di chi manca.
- Icona + testo, non solo colore.

**KPI** — da 4 a 5, riallineati:

| KPI | Colore | Nota |
|---|---|---|
| Da fare (tue) | rosso | ex "Richiedono un'azione da te" |
| In attesa di altri | giallo | nuovo |
| In corso | blu | scheduled + running + reporting |
| Completati | verde | |
| Bloccati / da sbloccare | grigio | opzionale |

**Legenda** identica in lista e dettaglio (stessa componente, stessi simboli).

---

### P3 — Dettaglio: una sola "next action" con proprietario (priorità alta, effort S)

- Sostituire il box `note` generico con una card "Prossimo passo" che contiene:
  - icona e colore semantico;
  - **"Azione richiesta a te"** oppure **"In attesa di: Cliente / PMO / Pentester / Manager"**;
  - una frase sola ("Firma e carica la manleva");
  - bottone primario che porta alla scheda giusta.
- La pill di stato accanto al titolo usa la stessa semantica.
- Le tab: `s-todo` → rosso, `s-wait` → giallo, `s-ok` → verde, `s-lock` → grigio.
  Oggi `s-wait` è blu: è l'incoerenza più visibile.
- La nota di attesa non deve essere rossa: se stai aspettando il cliente,
  è gialla e dice "In attesa del cliente dal 12/06".

---

### P4 — Motore unico di stato lato server (priorità alta, effort M)

Oggi la stessa verità è ricalcolata in quattro funzioni JS. Proposta:
esporre nel DTO del dettaglio (e della lista) un oggetto unico:

```json
"actionState": {
  "kind": "action",            // action | waiting | progress | done | blocked | neutral
  "owner": "Client",           // chi deve agire, o null
  "ownerLabel": "Cliente",
  "label": "Firma e carica la manleva",
  "tab": "docs",
  "since": "2026-06-12"
}
```

`TodoResolver` diventa l'unica fonte; `listStatus`, `detailStatus`, `tabState`
leggono solo il campo. Vantaggi: nessuna divergenza, test unitari sul workflow,
possibilità di aggiungere SLA e filtri senza toccare la UI.

Nota: per la lista serve una variante compatta (`actionState` per riga), perché
il `TodoResolver` attuale restituisce una sola azione. Si può aggiungere un
metodo `ResolveAll` o un campo calcolato nel DTO.

---

### P5 — Filtri e ordinamento per urgenza (priorità media, effort M)

- Chip filtro sopra la lista: **Da fare (n)** · **In attesa (n)** · **In corso (n)** · **Tutti**.
- Ordinamento predefinito: rosso → giallo → blu → verde, poi data di inizio.
- Ricerca per nome/cliente/codice.
- Contatore nel titolo: "3 engagement richiedono un'azione da te".

---

### P6 — Invecchiamento dell'attesa / SLA (priorità media, effort S/M)

- Mostrare da quanti giorni sei in attesa: "In attesa cliente da 6 giorni".
- Dopo la soglia (es. 5 giorni) il giallo diventa ambra scura (`#93370D`) e
  compare il testo "sollecita"; **non diventa rosso**, perché la palla non è tua.
- Per le azioni tue ferme da giorni, il rosso resta rosso ma aggiunge "in ritardo di N giorni".
- Riusa le regole di sollecito già presenti (`SolicitationService`).

---

### P7 — Microcopy e accessibilità (priorità alta, effort S)

- "Azione richiesta" → **"Da fare"**; "In attesa di altri" → **"In attesa di <ruolo>"**.
- "Proposta da valutare" → dipende dal ruolo: **"Da decidere (tu)"** per il
  Manager, **"In attesa dell'approvazione del Manager"** per cliente e PMO.
  Le proposte di date le inviano cliente o PMO; le approva solo il Manager.
- Ogni pill: `title`/`aria-label` con la frase estesa.
- `aria-live="polite"` sul toast (già presente) e sullo stato che cambia dopo un'azione.
- Focus visibile già buono; aggiungere focus management nel modale (focus al primo
  campo, ritorno al bottone che l'ha aperto, chiusura con Esc).
- Tabelle: `scope`/`caption` e header semantici; su mobile convertire le righe in card.

---

### P8 — Layout e densità (priorità media, effort M)

- **Dettaglio**: spostare Distribution list / Comunicazioni / Registro in una
  colonna secondaria o in un gruppo "Altro" separato dalle 5 tab principali.
  Ridurre la legenda duplicata (una sola, in alto).
- **Lista**: su mobile la tabella diventa una card per engagement; su desktop
  mantenere la tabella ma con colonna "Stato" più larga.
- Header sticky con filtro attivo e contatore azioni.
- Ridurre i riquadri annidati: oggi ci sono card dentro card (`renderOverview`
  + tab + note). Una sola gerarchia visiva.

---

### P9 — Stati di caricamento, errore e doppio invio (priorità alta, effort S)

- Skeleton loader per lista e dettaglio invece di "Caricamento…".
- Errore inline con bottone **Riprova**, non solo toast.
- Disabilitare il bottone durante la richiesta e mostrare spinner/testo
  "Salvataggio…"; riabilitarlo in `finally`.
- Conferma esplicita dopo azioni irreversibili (rifiuto proposta, pubblicazione).
- Vuoti utili: "Nessun engagement" + spiegazione + azione successiva.

---

### P10 — Chrome demo (priorità bassa, effort S)

- Raggruppare Ruolo + Tenant in un unico controllo "Modalità demo" con etichetta
  chiara, separato visivamente dal logo/prodotto.
- Banner demo più esplicito: "Dati fittizi · reset al riavvio".
- Evitare che il selettore ruolo sembri un controllo di produzione.

---

### P11 — Refresh palette brand (opzionale, effort S)

Mantenere navy + cyan per il brand, adottare la palette di stato standard
(quella sopra, derivata da scale accessibili). In alternativa, se vuoi un look
più "enterprise", si può passare a:
- brand `#0B1F3A` / `#0F4C81`, accento `#0E7490`;
- stati con le stesse tinte ma desaturate per aree grandi.

Non è necessario per la semantica: P1–P10 funzionano anche con la palette attuale.

---

## 4. Mappatura puntuale: stati attuali → nuovi

| Situazione attuale | Oggi | Nuovo |
|---|---|---|
| `Docs` – manleva/ordine mancanti | giallo "In attesa documenti" | **rosso "Da fare: firma manleva / registra ordine"** se puoi agire tu; **giallo "In attesa cliente/PMO"** altrimenti |
| `Planning` – proposta pending | blu "Proposta da valutare" | **rosso "Da decidere"** per il Manager; **giallo "In attesa dell'approvazione del Manager"** altrimenti |
| `Planning` – nessuna proposta | giallo "Da pianificare" | **rosso "Proponi date"** se puoi proporre; **giallo "In attesa PMO/cliente"** altrimenti |
| `Scope` | giallo "Preparazione in corso" | **rosso "Compila lo scope"** se puoi compilare; **giallo "In attesa cliente"** altrimenti |
| `Credentials` | giallo "Preparazione in corso" | **rosso "Fornisci le credenziali"** se puoi compilare; **giallo "In attesa cliente"** altrimenti |
| `Scheduled` | blu | **blu "Pianificato dal …"** (invariato) |
| `Running` | blu | **blu "In esecuzione"** (invariato) |
| `Reporting` – revisione pending | teal | **giallo "In attesa revisione manager"** (per il Pentester) / **rosso "Rivedi e approva"** (per il Manager) |
| `Reporting` – modifiche richieste | teal | **rosso "Aggiorna e reinvia"** (Pentester) |
| `Reporting` – approvato da pubblicare | teal | **rosso "Pubblica al cliente"** (Pentester) |
| `Reporting` – in redazione | teal | **blu "Report in redazione"** |
| `Done` | verde | **verde "Completato"** (invariato) |
| Sezione non disponibile | grigio con 🔒 | **grigio "Bloccato: …"** + motivo esplicito (prerequisito/permesso) |
| Proposta rifiutata | rosso `p-err` | **grigio/rosso scuro "Rifiutata"** con icona `×`, distinta dal rosso azione |
| Bozza report / proposta superata | grigio | **grigio neutro** (invariato) |

---

## 5. Mock testuale (lista, prima/dopo)

**Prima**

```
External Penetration Test · ENG-2026-001 · azione richiesta
Tipologia: Penetration Test   Avanzamento: ▬▬▬▭▭   [In attesa documenti]
```

**Dopo**

```
External Penetration Test                          [● Da fare: firma manleva]
ENG-2026-001 · Contoso · Penetration Test          [● In attesa: ordine PMO]
▬▬▬▭▭  Pianificazione bloccata                     1–5 giu
```

Con `Da fare` rosso, `In attesa` giallo, e la frase che dice esattamente chi
deve fare cosa.

---

## 6. Piano di implementazione consigliato

| Fase | Contenuto | Effort | Rischio | Valore |
|---|---|---|---|---|
| 1 | P1 + P2 + P3 (token, lista, KPI, dettaglio, tab, note) | S/M | Basso | Altissimo: risolve l'incoerenza e la richiesta giallo/rosso |
| 2 | P4 (stato unico lato server) + test | M | Medio | Alto: elimina la duplicazione |
| 3 | P5 + P6 (filtri, ordinamento, SLA) | M | Basso | Alto con molti engagement |
| 4 | P7 + P9 (microcopy, a11y, loading/errori) | S/M | Basso | Medio-alto |
| 5 | P8 + P10 + P11 (layout, demo chrome, palette) | M | Basso | Medio |

**Raccomandazione**: partire dalla fase 1, che è autocontenuta in `index.html`
e non tocca API né database. Poi la fase 2 per consolidare la logica.

---

## 7. Punti da decidere

1. Il rosso deve essere **pieno** (pill con sfondo rosso e testo bianco) o
   **soft** (sfondo rosa chiaro, testo rosso scuro)? Consiglio soft: meno
   affaticante e più leggibile su liste lunghe.
2. "Bloccato dall'utente" include anche i casi in cui l'utente **ha i permessi
   ma non ha ancora compilato**? Consiglio sì: è la definizione di "palla a te".
3. Il rosso va usato anche per **rifiuti/errori**? Consiglio: stessa famiglia,
   ma icona `×` e testo esplicito, mai da solo.
4. Vuoi mantenere la palette brand attuale o approfittarne per un refresh (P11)?

---

## 8. Stato di implementazione (aggiornato)

Tutte le proposte P1–P11 sono state applicate. Scelte adottate:

- Rosso **soft** (`#B42318` su `#FEE4E2`), come consigliato.
- Giallo "in attesa" più giallo (`#7A5B00` su `#FFF8C5`) per non confonderlo col rosso.
- Microcopy formale: "Azione richiesta a te" / "In attesa di: <ruolo>"; niente "tocca a".
- Scope e credenziali compilabili appena completi manleva e ordine, anche prima
  dell'approvazione della pianificazione (era richiesta la proposta accettata).
- "Bloccato" include sia i prerequisiti mancanti sia i permessi.
- Rifiuti ed errori usano la stessa famiglia del rosso azione ma con icona e
  testo espliciti (`.p-negative`).
- Palette brand mantenuta, con accento leggermente desaturato per coerenza.
- Icone **Bootstrap Icons** (licenza MIT) come sprite SVG inline in `index.html`:
  nessuna dipendenza esterna, colore ereditato da `currentColor`, stesse icone
  per fasi, check e ricerca in tutta l'app.
- Terminologia: "provider" non è un ruolo e non compare più nel testo. L'ente che
  eroga il servizio si chiama "Il mio ente"; gli utenti hanno i ruoli PMO,
  Pentester, Manager, Auditor.
- Pianificazione: **propongono le date cliente o PMO**, **approva o rifiuta solo
  il Manager** (vale anche per le richieste di riprogrammazione). Il controllo è
  nel workflow e nell'endpoint `decide`, non solo nella UI.

### Cosa è cambiato

| Proposta | Implementazione |
|---|---|
| P1 | Token `--st-*` in `:root`; `.pill`, `.tab`, `.note`, KPI e legenda usano solo token semantici |
| P2 | Lista con pill di stato + frase + "In attesa di: <ruolo>" + invecchiamento; 5 KPI; legenda uguale in lista e dettaglio |
| P3 | Card "Prossimo passo" unica (`nextAction`) con icona, proprietario e bottone; tab con stato dal server |
| P4 | `TodoResolver.ResolveAction` e `ResolveTabs` in `EngagementWorkflow.cs`; DTO con `actionState` e `tabStates`; la UI non ricalcola più lo stato |
| P5 | Chip filtro (Tutti / Da fare / In attesa / In corso / Completati), ricerca live, ordinamento per urgenza |
| P6 | `since` nell'`actionState`: "da N giorni", ambra scura oltre 5 giorni in attesa, "in ritardo" per le azioni |
| P7 | Microcopy formale ("Azione richiesta a te", "In attesa di <ruolo>", "Da fare"), `aria-label`/`title` su tab e pill, `aria-busy` sui bottoni, `caption`/`scope` nelle tabelle, legenda con icona + parola |
| P8 | Tab secondarie (Distribution list, Comunicazioni, Registro) sotto "Altro"; tabella che diventa card su mobile; gerarchia visiva ridotta |
| P9 | Skeleton in lista, errore inline con "Riprova", spinner e blocco dei bottoni durante le richieste |
| P10 | Controllo "Demo" unico (Profilo + Cliente) separato dal logo, banner demo esplicito |
| P11 | Palette invariata; accento `#0e9fb5` |

### File toccati

- `EngagementPortal.Web/Services/EngagementWorkflow.cs` — motore di stato unico
  (`ResolveAction`, `ResolveTabs`, `ActionState`, `TabState`, helper di permesso).
- `EngagementPortal.Web/Program.cs` — `actionState` e `tabStates` nei DTO di
  lista e dettaglio; permessi delegati al workflow.
- `EngagementPortal.Web/wwwroot/index.html` — token, componenti, lista,
  dettaglio, filtri, skeleton, accessibilità.
- `EngagementPortal.Tests/Unit/StateResolverTests.cs` — 12 test sul nuovo motore
  (chi agisce, chi attende, tab bloccate, invecchiamento).

### Verifica

- `dotnet test EngagementPortal.sln -c Release` → 38 test superati.
- Controllo manuale via API per i cinque ruoli e via browser su lista, dettaglio,
  tab, proposta pending, revisione report: colori e proprietari coerenti.

### Limiti noti

- Il DTO di lista calcola `actionState` per riga: con migliaia di engagement
  conviene una proiezione dedicata o una cache.
- L'invecchiamento è calcolato sul giorno corrente del server (`IDateProvider`),
  quindi in demo dipende dalla data di sistema.
- La tabella mobile usa `data-label`; se si aggiungono colonne vanno aggiunte
  anche le etichette.

---

## 9. Dettaglio engagement: layout implementato (sidebar + workspace)

La legenda degli stati sotto i tab ripeteva le icone già presenti nei tab e
creava una terza riga di navigazione. Le tre varianti di confronto restano nel
mockup `detail-layout-proposals.html`; è stata **implementata la variante C**.

**Com'è fatto ora**

- Colonna sinistra (320 px, `position:sticky`) con codice e tipologia, titolo,
  pill di stato, avanzamento, riepilogo (cliente, responsabile, periodo,
  ordine) e accessi secondari (DL, comunicazioni, registro).
- Colonna destra con il riquadro "azione richiesta" (CTA) e i 5 tab; il
  contenuto inizia subito sotto.
- La legenda non è più una riga della pagina: sta in fondo alla sidebar dentro
  un `<details>` ("Cosa significano i simboli?"), quindi non ruba spazio ma
  resta consultabile.

**Avanzamento leggibile**

- Desktop: stepper verticale, un passo per riga, binario a sinistra che si
  colora man mano. Cerchio da 26 px con numero; i passi completati hanno la
  spunta e il binario in accent, quello corrente un anello blu, i futuri sono
  in grigio. Titolo e sottotitolo vanno a capo senza schiacciarsi
  (`minmax(0,1fr)`), quindi anche i nomi lunghi restano leggibili.
- Sotto i 1000 px la sidebar si impila sopra il contenuto: la lista verticale
  lascerebbe troppo spazio vuoto, quindi viene sostituita da una barra
  segmentata compatta con "Passo 4 di 6" e il nome del passo corrente.
- Il contatore "4/6" in testata all'avanzamento indica la posizione nella fase,
  non le fasi completate.

---

## 10. Homepage: layout implementato (variante B, azione prima)

La lista engagement ripeteva i conteggi in tre punti (card KPI, chip di filtro,
legenda), con cinque card KPI che spingevano la tabella a metà pagina. È stata
**implementata la variante B**, con l'aggiunta della colonna **Fase**. Le
varianti A e C restano nel mockup `homepage-proposals.html`.

**Com'è fatta ora**

- Testata con titolo, perimetro, azioni (Clienti e utenze, Nuovo engagement).
- Quattro tessere che sono anche filtri: **Da gestire**, **In attesa di altri**,
  **A rischio avvio**, **Prossimo avvio** (data).
- Due colonne (300 px + resto; sotto i 1200 px si impilano):
  - **Da gestire ora**: fino a 4 card con nome, cliente/codice/tipologia,
    azione richiesta, giorni di attesa o allerta di ritardo e CTA diretta alla
    scheda giusta ("Compila lo scope", "Apri i documenti", …).
  - **In attesa di altri**: elenco compatto con responsabile e giorni.
  - **Tutti gli engagement**: tabella compatta con ricerca, filtro e
    ordinamento nella toolbar e "Solleciti automatici" come link con badge.
- Colonna **Fase** compatta: barra a 6 segmenti più il nome del passo corrente
  (circa 100 px), con tooltip "Passo N di 6: nome — descrizione". Il passo
  completato mostra "Completato".
- La legenda è stata rimossa: gli stati sono scritti nelle righe.

**Scelte di dettaglio**

- La tabella usa Stato compatto: pill, responsabile (solo per le attese) e
  giorni; l'azione estesa vive nelle card "Da gestire ora".
- La tipologia è un tag sotto il codice, per liberare la colonna Cliente.
- Il filtro "A rischio" usa `preparationAlert` (scope/credenziali mancanti a
  due settimane dall'avvio), la stessa fonte della colonna Ritardo.
- Ordinamento: priorità (default), avvio più vicino, cliente, nome.

**Perché B**: mette in cima ciò che richiede un intervento e lascia la tabella
come consultazione completa; le tessere sostituiscono KPI, chip e legenda con
una sola riga. La A resta più adatta se si vuole una home identica per tutti i
profili; la C se serve una vista di pipeline.

**Responsive**

- Sotto i 1200 px le due colonne si impilano; sotto i 900 px le tessere vanno
  su 2 colonne e sotto i 700 la colonna sinistra si allinea in verticale.
- Sotto gli 820 px la tabella diventa una lista di schede (`data-label`); sotto
  i 760 l'header si compatta (logo + utente, selettori demo a tutta larghezza) e
  sotto i 380 spariscono avatar e ruolo per guadagnare una riga.
- Su monitor larghi il contenuto cresce a 1360/1520/1680 px; la colonna sinistra
  passa a 340/360 px e il workspace del dettaglio a 360/380 px. La pagina
  clienti passa a 2 colonne oltre 1100 px e a 3 oltre 2000 px.



