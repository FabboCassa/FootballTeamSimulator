# Informativa sulla privacy — Football Team Simulator

**Bozza. Falla leggere a un legale prima di pubblicarla.** Il contenuto tecnico e ricavato
da cio che il codice fa davvero (vedi `data-safety.md`), che e la parte difficile; la
forma giuridica va verificata da un professionista, in particolare le parti GDPR, dato che
lo sviluppatore e stabilito in Italia.

_Ultimo aggiornamento: [DATA] · Versione [X.Y.Z]_

---

## Chi e il titolare

Football Team Simulator ("il Gioco") e sviluppato e gestito da Fabio Casarini ("noi"),
sviluppatore individuale stabilito in Italia. Ai fini del Regolamento generale sulla
protezione dei dati (GDPR) siamo il titolare del trattamento.

Contatto: [EMAIL DI CONTATTO]

## In breve

Se giochi la carriera in singolo non raccogliamo nulla. Il Gioco gira sul tuo dispositivo
e il salvataggio non lo lascia mai.

Se crei un account per giocare online — leghe private con gli amici o classifica pubblica
— conserviamo il minimo necessario a farlo funzionare: indirizzo email, password
cifrata, il nome che scegli, i dati di gioco e una forma cifrata del tuo indirizzo IP, che
serve a impedire che una stessa persona faccia giocare piu account l'uno contro l'altro.

Non mostriamo pubblicita. Non usiamo servizi di analisi o tracciamento. Non vendiamo ne
cediamo i tuoi dati a nessuno.

## Cosa raccogliamo e perche

Trattiamo dati solo se scegli di creare un account.

| Cosa | Perche | Base giuridica (GDPR) |
|---|---|---|
| Indirizzo email | Identificare l'account, farti accedere e inviarti comunicazioni di servizio come il ripristino della password | Esecuzione di un contratto (art. 6(1)(b)) |
| Password | Farti accedere. Conservata solo come hash crittografico: non l'abbiamo e non possiamo recuperarla | Esecuzione di un contratto |
| Nome visualizzato | Mostrato agli altri giocatori in lega, in classifica e nel palmares | Esecuzione di un contratto |
| Dati di gioco (club, rose, trasferimenti, risultati, punteggi, premi) | Far funzionare la partita che stai giocando | Esecuzione di un contratto |
| Forma cifrata e salata del tuo indirizzo IP, e un identificativo di dispositivo facoltativo | Individuare piu account gestiti dalla stessa persona, che altrimenti permetterebbero di truccare una lega ai danni di chi gioca onestamente | Legittimo interesse (art. 6(1)(f)): tenere la competizione leale |
| Token per le notifiche push, se le autorizzi | Avvisarti che sei stato superato a un'asta o che la tua partita sta per iniziare | Consenso (art. 6(1)(a)), che concedi nelle impostazioni del dispositivo e puoi revocare li |
| Segnalazioni che invii e verifiche automatiche di integrita | Esaminare sospetti casi di collusione o abuso | Legittimo interesse |

**Il tuo indirizzo IP non viene mai conservato in chiaro.** Viene cifrato con un sale
segreto prima di essere scritto, e possiamo ruotare quel sale: cosi facendo il legame fra
i dati storici e qualunque indirizzo viene reciso in modo definitivo.

## Cosa non raccogliamo

Nessun identificativo pubblicitario. Nessun dato di posizione. Nessun accesso a contatti,
foto, microfono o fotocamera. Nessun dato di pagamento: il Gioco non contiene acquisti.
Nessun software di pubblicita, attribuzione o analisi di terze parti.

## Chi altro vede i tuoi dati

Un solo servizio, e solo se le notifiche push sono attive:

- **Google Firebase Cloud Messaging**, che riceve il token di notifica del tuo dispositivo
  e il testo della notifica per poterla consegnare. A quel trattamento si applicano le
  condizioni sulla privacy di Google.

Oltre a questo i tuoi dati non sono condivisi con nessuno. Potremmo comunicarli se
obbligati per legge, e te lo diremmo salvo che la legge lo vieti.

I nostri server si trovano in [LUOGO DI HOSTING — da completare prima della
pubblicazione]. Se fosse fuori dall'Unione europea, i trasferimenti avvengono con le
garanzie previste dal Capo V del GDPR.

## Per quanto tempo li conserviamo

- Dati dell'account: finche l'account esiste.
- Token di rinnovo sessione: 30 giorni, o fino alla disconnessione.
- Dati di integrita (segnali cifrati, flag, segnalazioni): fino a 12 mesi, poi cancellati.
  Sopravvivono di proposito alla lega a cui si riferiscono: la traccia di un trasferimento
  truccato non serve a nulla se sparisce insieme alla lega.
- Dati di gioco: finche l'account esiste.

Quando cancelli l'account, tutto quanto sopra viene eliminato, salvo cio che siamo tenuti
per legge a conservare.

## Cancellare l'account

Puoi cancellare l'account quando vuoi, dall'interno del Gioco (Account -> Elimina account)
oppure dalla pagina pubblica `<sito>/delete-account.html`. La cancellazione e immediata e
non e reversibile. Elimina email, password, profilo allenatore, sessioni, dispositivi
registrati, iscrizioni alle leghe private e le impronte cifrate usate contro gli account
multipli; una lega privata rimasta senza membri viene chiusa insieme all'account.

Due cose restano, non piu collegate a te: i risultati delle stagioni classificate gia
giocate, conservati sotto un identificativo che non appartiene a nessuno perche le
classifiche e i palmares degli altri allenatori restino corretti, e la squadra che stavi
allenando in una stagione in corso, che prosegue gestita dal computer.

## I tuoi diritti

Ai sensi del GDPR puoi chiederci una copia dei tuoi dati, la loro rettifica o
cancellazione, la limitazione o l'opposizione al trattamento, e la portabilita. Puoi
inoltre proporre reclamo all'autorita di controllo: in Italia, il Garante per la
protezione dei dati personali.

Per esercitare questi diritti scrivi a [EMAIL DI CONTATTO]. Puoi cancellare l'account
dall'interno del Gioco in qualsiasi momento, con gli effetti descritti sopra.

## Minori

Il Gioco non e rivolto a minori di 13 anni (o di 16 dove la legge locale fissa quella
soglia) e non raccogliamo consapevolmente i loro dati. Se ritieni che un minore abbia
creato un account, scrivici e lo cancelleremo.

## Dati salvati sul tuo dispositivo

Il Gioco conserva il salvataggio della carriera in singolo e le impostazioni sul tuo
dispositivo: nel browser tramite IndexedDB, su computer e mobile nella cartella
dell'applicazione. Questi dati non arrivano mai a noi. Svuotare i dati del browser o
disinstallare l'app li elimina.

## Modifiche

Se modifichiamo questa informativa aggiorneremo la data in alto e, per i cambiamenti
rilevanti, te lo comunicheremo nel Gioco al primo accesso successivo.
