# Cyber Engagement Portal — To-do list

Backlog delle funzionalità. Le voci **fatte** restano in fondo come riferimento.

---

## 1. Retest e report dei retest — ✅ FATTO

Due passi in coda all'esecuzione, presenti **solo se il recheck è previsto**.

- **Retest**: il **cliente** richiede il retest **a fix completati** e **una sola
  volta** (`POST /engagements/{id}/retest/request`; seconda richiesta → 409). Le
  date seguono lo stesso giro del planning: proposta di **PMO o cliente**
  (`/retest/propose`, avvio non nel passato) e **approvazione del Manager**
  (`/retest/decide`).
- **Report di retest**: il Pentester conclude il retest e carica il report
  (`/retest/report`), poi lo **invia al cliente senza approvazione**
  (`/retest/send`; secondo invio → 409). L'attività passa a **Concluso**.
- Nuovo template bilingue `retestreport` ("Invio report di retest") in
  Amministrazione › Template mail e al livello engagement in Mailing list.
- Fasi nuove: `Retest` (passo 7) e `RetestReporting` (passo 8); stepper, KPI e
  registro seguono. Il registro riporta l'invio con il testo del template.
- Seed: **ENG-2026-006** è il percorso completo (richiesta → date → report →
  invio); **ENG-2026-015** è in attesa della richiesta del cliente (recheck
  previsto, report pubblicato); i Red Team (004, 013) sono **senza recheck**.
- Test: 4 casi nuovi (recheck spento, attesa della richiesta, proposta in attesa
  del Manager, invio senza revisione). 81/81 verdi.

### Ancora aperto su questa voce

- **Calendario**: le date del retest non sono ancora mostrate come evento
  distinto dell'engagement.
- **Solleciti**: non c'è una regola automatica "retest in ritardo"; resta il
  sollecito manuale dall'engagement (l'attesa del retest è fra i task
  sollecitabili).
- **Esito del retest**: il report è un deliverable di tipo `Retest` con
  descrizione libera; non ci sono campi strutturati (vulnerabilità risolte o
  residue) né un esito per singolo finding.
- **Chiude senza retest**: se il cliente non chiede il retest l'engagement resta
  in attesa; manca un'azione esplicita "chiudi senza retest".

---

## 2. Recheck previsto nel Setup — ✅ FATTO

- Campo **"Recheck previsto (retest a fine attività)"** nel **Setup**, con
  default dal tipo di attività e pulsante *Applica i default del tipo*.
- **Modificabile fino alla richiesta di retest** del cliente: dopo, il salvataggio
  del Setup che prova a cambiarlo viene rifiutato con un messaggio chiaro.
- Traccia **chi** e **quando** ha deciso (`RetestDecidedByName/Date`, mostrati nel
  pannello del retest).
- Se il flag è spento, i due passi di retest **non esistono**: né stepper, né
  attese, né azioni.

---

## 3. Sezione "Attività": catalogo dei tipi — ✅ FATTO

- Nuova scheda **Amministrazione › Tipi di attività** (Manager e PMO).
- Cinque tipi (granularità confermata): Penetration Test, Vulnerability
  Assessment, Red Team, Code Review, Cloud Security. Le chiavi sono fisse; si
  modificano **etichetta, sigla, descrizione, deliverable previsti e recheck
  previsto**, con *Ripristina default*.
- Default di recheck: **Red Team senza retest**, gli altri con retest previsto.
- Storage negli *app settings* (`activity-types`), quindi il catalogo è
  modificabile senza toccare il codice; endpoint `GET /api/activity-types` e
  `PUT /api/activity-types/{key}`.

### Ancora aperto su questa voce

- **Propagazione**: i default valgono per gli engagement nuovi e per chi preme
  *Applica i default del tipo*; non riscrivono le attività esistenti.
- **Granularità**: i sottotipi (Web Application, Mobile, Infrastructure, OT) non
  sono previsti.
- **Altri default per tipo**: durata tipica, template di scope, team e liste
  standard non sono ancora nel catalogo.

---

## 4. Punti minori aperti

- **File di anteprima con i vecchi clienti**: `mailing-list-proposals.html`,
  `admin-layout-v2.html`, `admin-layout-proposals.html`, `calendar-proposals.html`,
  `template-ambito-opzioni.html` e le altre proposte contengono ancora
  Contoso/Fabrikam/Nova Bank. Sono mockup storici, non dati dell'app: da
  riallineare ad Anthropic/Nvidia solo se servono ancora.
