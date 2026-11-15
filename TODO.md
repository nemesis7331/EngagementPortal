# Cyber Engagement Portal — To-do list

## Revisione del codice — Fase 1 ✅ FATTA

Quattro agenti hanno analizzato il codice (codice morto client e server, bug,
mantenibilità). La Fase 1 è stata completata.

### Bug corretti
- **Retest**: il report non si può più sovrascrivere — 409 se già caricato o se
  già inviato al cliente (prima l'invio veniva perso); la proposta di date non
  cancella più quella pendente (409); durata massima 60 giorni come il planning.
- **Chiusura senza retest**: nuovo `POST /engagements/{id}/retest/close` per
  PMO/Manager, così l'attività non resta in attesa per sempre se il cliente non
  chiede mai il retest.
- **Recheck**: il flag non si può più cambiare su un'attività conclusa, né dopo
  la richiesta del cliente.
- **Routing**: una seconda navigazione durante un caricamento non viene più
  persa; l'identità della demo (`role`/`user`/`client`) viene letta dall'URL,
  quindi un refresh non fa più ricadere sul profilo di default.
- **Clienti**: la ricerca filtra anche il pannello (prima restava il cliente non
  più visibile).
- **Mailing list**: il cliente scelto in header filtra l'elenco; cambiando
  cliente in Amministrazione l'elenco si ricarica.
- **Concorrenza e tempo**: `_enabled` dei solleciti è una `ConcurrentDictionary`;
  base temporale unica (`UtcDateTime`); `clock` al posto di `UtcNow` dove
  serviva; `StringComparison` sul confronto dei nomi.
- **Validazioni**: email uniforme (`IsEmail`), sonda `/health/ready` senza dati,
  email di consegna solo PMO (allineata alla matrice permessi).

### Codice morto rimosso
- Client: 8 funzioni mai chiamate (`clientAvatar`, `statusText`, `tabReason`,
  `vstepper`, `cprogress`, `kpiPhaseLabel`, `runReason`, `isEditing`), **44
  regole CSS** di layout abbandonati, la `.mini i` duplicata e la media query
  `1500px` ripetuta.
- Server: `MailTemplates.Type`, `Access.VisibleClients`, `IUserRepository.Remove`
  e le due implementazioni, `Engagement.CredentialDeliveryMethod`,
  `ResolvedMailTemplate.Template`, due parametri di endpoint mai usati;
  `SolicitationRule.CountBusinessDays` ora **comanda davvero** il calcolo.

### Oggetti standard collegati
- **7C**: l'elenco report usa icona per tipologia, nome, versione e stato.
- **2B3**: la mailing list usa la pastiglia compatta dell'engagement (tipo, nome,
  logo cliente, anello di avanzamento).

### Verifiche
- Build: 0 errori, 0 avvisi. Test: **82/82**.
- Nota: il bug del report di retest è stato riprodotto e confermato nei dati
  prima della correzione.

---

## Fase 2 — da fare (manutenibilità)

1. **`GET /api/meta`** con deliverable, tipi di attività, tipi di template
   (con `icon` e `listKind`), passi del workflow, organizzazioni e soglie; il JS
   li legge all'avvio e si eliminano le costanti duplicate. **Una costante per
   volta, con fallback.**
2. **Estrarre gli endpoint** da `Program.cs` (~40 route) in moduli per area.
3. **Estrarre CSS e JS** da `index.html` (un solo `<style>` e un solo
   `<script>`), poi **ESLint + `tsc --checkJs`** in CI: oggi non esiste alcun
   controllo statico sul JS.
4. **Test HTTP** con `WebApplicationFactory` sui casi critici (403/200, retest,
   financials): le lambda degli endpoint non sono coperte.

## Fase 3 — da fare (più impegnativa)

5. Spezzare le funzioni di render lunghe (`renderDetail` ~180 righe,
   `renderKpi` → oggi `dash*` della Dashboard, `tabReports` ~130).
6. README non normativo su RACI, elenco endpoint e dataset (invecchiano a ogni
   modifica).

## Altri punti aperti (dal backlog funzionale)

- **Calendario**: le date del retest non sono ancora un evento distinto.
- **Solleciti**: nessuna regola automatica "retest in ritardo" (resta il
  sollecito manuale).
- **Esito del retest**: il report è un deliverable con descrizione libera, senza
  campi strutturati per i finding.
- **Propagazione dei default per tipo**: valgono per i nuovi engagement e per
  chi preme *Applica i default*.
- **File di anteprima con i vecchi clienti** in `wwwroot` (mockup storici).
