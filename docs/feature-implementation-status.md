# Implementatiestatus van de 80 voorstellen

Build: **2.2.0-preview.1**, 15 november 2026. Deze lijst beschrijft de implementatie in deze checkout. De app is nog niet volledig op echte hardware gekwalificeerd. Een geslaagde gesimuleerde test bewijst geen werking van een monitorfirmware, HDR-driver of laptop.

**Toegevoegd** betekent dat de bediening, opslag en uitvoeringsroute aanwezig zijn. **Hardware** betekent dat de route is toegevoegd, maar fysieke schrijfproeven en de relevante hardwarematrix nog nodig zijn. **Deels** vermeldt een concrete resterende beperking. **Voorbereid** vereist een extern middel of releasebewijs.

Alle nieuwe instellingen staan onder **Beheer / Workspace** in de bovenste werkbalk. Kleuren, preview en exacte invoer staan in het hoofdvenster. Bestaande instellingen blijven via het tandwiel bereikbaar.

| ID | Status | Uitwerking en toegang |
|---|---|---|
| D01 | Toegevoegd | Weergave: licht, donker of Windows volgen. |
| D02 | Toegevoegd | Weergave: gevalideerde hex-accentkleur. |
| D03 | Toegevoegd | Compact/ruim, aangepast knoppadding en icoongrootte. |
| D04 | Toegevoegd | Schaalbaar hoofdvenster, flexibele editor en scrollbare lijsten. |
| D05 | Toegevoegd | Optionele tekstlabels Toepassen/Opslaan/Herstellen. |
| D06 | Toegevoegd | Status met ernst, icoon en leesbare melding; succes heeft geen waarschuwingsteken. |
| D07 | Toegevoegd | Bezig-indicator tijdens profielwerk en preview. |
| D08 | Toegevoegd | Exacte waarden; ongeldige invoer blijft zichtbaar en blokkeert toepassen/opslaan. |
| D09 | Toegevoegd | Herstellen per kleurregelaar onder exacte invoer. |
| D10 | Toegevoegd | Uitleg bij kleurhelderheid, gamma, backlight en vibrance. |
| D11 | Toegevoegd | Profielen: eigen icoonteken en kleurmarkering. |
| D12 | Toegevoegd | Profiel- en schermfavorieten bovenaan. |
| D13 | Toegevoegd | Zoeken op naam, scherm of applicatiekoppeling. |
| D14 | Toegevoegd | Tab-bediening en Ctrl+S, Ctrl+Enter, Ctrl+Z, Ctrl+Shift+Z; opname van sneltoetsen wordt ontzien. Handmatige volledige toetsenbordacceptatie blijft nodig. |
| D15 | Toegevoegd | Automation-namen, bereiken, actieve status en live statusmelding; nog geen volledige screenreadersessie uitgevoerd. |
| D16 | Toegevoegd | Systeemkleuren bij Windows-contrastthema; ernst ook met iconen/tekst. Fysieke contrastthematest nog nodig. |
| D17 | Toegevoegd | Tekstschaal 100–200%, scrollbare inhoud; renders inclusief 200%. Niet elke DPI-/taalcombinatie getest. |
| D18 | Toegevoegd | Minder beweging; optionele kleurovergangen worden overgeslagen. |
| D19 | Deels | Nederlands/Engels via centrale resources; sommige technische foutmeldingen en rapportteksten blijven Engels. |
| D20 | Toegevoegd | Eerste-start-uitleg en opnieuw te openen introductie. |
| P01 | Toegevoegd | Maximaal 100 profielen per monitor; import hanteert dezelfde grens. |
| P02 | Toegevoegd | Profielcontextmenu: omhoog/omlaag, blijvend opgeslagen volgorde. |
| P03 | Hardware | Preview van 15 seconden; behouden of terugzetten; sluiting annuleert preview. Terugval met fakes getest. |
| P04 | Hardware | Oorspronkelijke gamma/vibrance van deze sessie terugzetten. Geen herstel over app-herstarts. |
| P05 | Hardware | Vergelijken vasthouden met muis of toetsenbord en terugkeren bij loslaten. |
| P06 | Toegevoegd | Maximaal 100 undo-/redo-stappen inclusief RGB en temperatuur. |
| P07 | Toegevoegd | Kopieer opgeslagen kleuren naar ander scherm; waarschuwing voor verschillende panelen; geen gekopieerde triggers. |
| P08 | Hardware | Benoemde sets met snapshots en terugval bij gedeeltelijk mislukte toepassing. |
| P09 | Toegevoegd | Referentieprofiel per monitor als terugval voor appregels. |
| P10 | Toegevoegd | Bewerkbare uitgangspunten Werk/Film/Avond/Gaming; geen kalibratieclaims. |
| P11 | Hardware | RGB-gain en benaderende temperatuur-tint via standaard gamma; geen gekalibreerd witpunt of HDR-kleurbackend. |
| P12 | Toegevoegd | Notities, icoon, favoriet en wijzigingsdatum. |
| A01 | Toegevoegd | Pauze tot herstart, aantal minuten of expliciet tijdstip. |
| A02 | Toegevoegd | Meerdere uitvoerbare applicaties per profiel. |
| A03 | Toegevoegd | Prioriteit voor appregels; gelijke prioriteit kiest niet willekeurig. Handmatige acties en versiecontroles beschermen tegen oude retries. |
| A04 | Toegevoegd | Instelbare stabiele-focusvertraging. |
| A05 | Hardware | Fullscreen-filter op venster-/schermrechthoek; geen garantie voor iedere exclusive-fullscreen game. |
| A06 | Toegevoegd | Weekdagen per helderheidsregel. |
| A07 | Toegevoegd | Alle of één geselecteerde monitor per regel. |
| A08 | Hardware | Begrensde helderheids- en kleurovergangen; topologiewijzigingen stoppen kleurwerk. |
| A09 | Toegevoegd | Zonsopkomst/-ondergang met handmatig ingevoerde coördinaten en offset; geen automatische locatie-opvraag. |
| A10 | Hardware | AC/batterijfilter; modelregels getest, laptopwissel niet fysiek getest. |
| A11 | Hardware | Instelbaar opnieuw toepassen na hervatten/aansluiten. |
| A12 | Toegevoegd | Foreground WinEvent-hook met polling als terugval. |
| H01 | Toegevoegd | Hardware: read-only scan van beschikbare routes en gerapporteerde VCP-functies. |
| H02 | Hardware | Actieve advanced color/HDR/WCG blokkeert legacy gamma. Op deze pc alleen SDR waargenomen. |
| H03 | Hardware | NVIDIA herdetectie, verouderde handles opruimen, begrensde herstelpogingen en verversactie. Live driververvanging niet gereproduceerd. |
| H04 | Toegevoegd | Handmatig opgeslagen losgekoppeld scherm koppelen aan aangesloten paneel, met snapshot vooraf. |
| H05 | Hardware | Capabilitystring en VCP-opties uitlezen; alleen geadverteerde toegestane functies schrijfbaar. Leesroute op Dell gecontroleerd. |
| H06 | Hardware | VCP-ingangskeuze wanneer geadverteerd; geen fysieke wissel uitgevoerd. |
| H07 | Hardware | Fysiek contrast en RGB-gain via VCP; alleen leesproeven uitgevoerd. |
| H08 | Deels | AMD ADLX/Intel IGCL onderzocht en beperking zichtbaar gemaakt. Extra AMD-/Intel-saturatiebackends ontbreken. Alleen deze NVIDIA-pc beschikbaar; Windows-gamma blijft afhankelijk van compatibele drivers. Zie vendor-color-backends.md. |
| H09 | Hardware | Afzonderlijke WMI-route met exacte panelinstance, begrensde cache en transportinvalidatie; alleen injecteerbare fake getest. |
| H10 | Toegevoegd | Lokale compatibiliteitsnotities per monitorverbinding. |
| T01 | Hardware | Profielenmenu direct vanuit de tray. |
| T02 | Toegevoegd | Benoemde groepen in monitorinstellingen; alleen gekoppelde groep volgt. |
| T03 | Toegevoegd | Helderheidsoffsets per scherm binnen een groep. |
| T04 | Toegevoegd | Minimum/maximum toegepast op popup, automatisering en ruwe helderheidscommando's. |
| T05 | Toegevoegd | Sneltoetsdoel: alle schermen, gekozen scherm of onder de muis. |
| T06 | Toegevoegd | Optionele trayballon na profiel-/helderheidssneltoets; Windows kan meldingen onderdrukken. |
| T07 | Toegevoegd | Popup vastzetten, volgorde en optionele schermdetails. |
| T08 | Hardware | VCP D6 voor één geadverteerd scherm. Herstel kan de fysieke aan/uitknop vereisen; slapende monitoren accepteren niet altijd softwarecommando's. |
| R01 | Toegevoegd | Diagnostiek en hardwarepaneel met backend, driver- en detectiestatus. |
| R02 | Toegevoegd | Begrensde sessiegeschiedenis van hoofdvensterstatussen met tijdstip. |
| R03 | Toegevoegd | Lokaal rapport met preview; schermnamen en apparaatpaden standaard weggelaten. Geen automatische verzending. |
| R04 | Toegevoegd | Read-only zelftest; VCP-schrijven is een aparte expliciete actie. |
| R05 | Hardware | Eén scherm opnieuw verbinden en kleurbackend verversen. |
| R06 | Hardware | Optionele gamma-observer meldt externe wijziging zonder corrigerende schrijfstrijd. |
| R07 | Toegevoegd | Opstartvertraging van 0–120 seconden. |
| R08 | Toegevoegd | Lokaal versie-/driverlogboek in diagnostische logs. |
| B01 | Toegevoegd | Importpreview en capaciteit per monitor vóór samenvoegen. |
| B02 | Toegevoegd | Profielen selecteren; optioneel applicatievoorkeuren meenemen; niet-geselecteerde setleden uitgesloten. |
| B03 | Toegevoegd | Begrensde snapshots, maximaal één automatische snapshot/minuut; vorige-save-backup blijft bestaan. |
| B04 | Toegevoegd | Snapshot kiezen, per profiel verschillen in kleuren/metadata/regels en voor/na-aantallen; selectief herstel via merge, zonder bestaande profielen te overschrijven. |
| B05 | Toegevoegd | Portable/per-user voorbereiden, locatie zichtbaar; herstart nodig. Bestaande configuratie wordt beschermd. |
| B06 | Toegevoegd | Meegeleverde CLI: list en apply via beperkte named pipe voor dezelfde Windows-gebruiker/sessie. Protocol getest; live serverinteractie nog handmatig kwalificeren. |
| Q01 | Toegevoegd | Expliciete updatecontrole bij publieke GitHub-releases met release-noteslink. Geen automatische installatie. |
| Q02 | Voorbereid | Certificaatselectie, timestamp, Authenticode-verificatie en verplichte ondertekening voor stabiele publicatie. Geen certificaat beschikbaar; deze preview is ongetekend. |
| Q03 | Toegevoegd | Stabiel/testkeuze, herkenbaar previewversienummer, prerelease-publicatie en beschermde backup vóór dataformatwijziging. |
| Q04 | Voorbereid | Hardwarematrix plus vereiste releasebewijsvalidator. Werkelijke matrix is nog niet afgerond. |

## Verificatie en vindplaatsen

Regressies staan in `tests/DisplayFX.RegressionTests`, inclusief `FeatureExpansionRegressionChecks`, bestaande fout-/concurrentietests en losse WPF-renders. Thema's en taal staan in `AppearanceController`/`LocalizationController`; uitgebreide bediening in `Interface/Workspace`; preview/setlogica in de partials van `Interface/Shell`; transporten in `MonitorHardwareFeatures`/`InternalPanelBrightness`.

Op deze NVIDIA-pc zijn alleen uitleesproeven uitgevoerd: twee SDR-uitgangen, leesbare NVIDIA-vibrance en helderheid. De Dell-uitgang adverteert backlight, contrast, RGB, invoer en power. Er zijn bij deze controle geen fysieke instellingen geschreven, geen monitoren uitgeschakeld en geen instellingen van de geïnstalleerde app vervangen.

Zie [hardwaretestmatrix](hardware-test-matrix.md), [release-informatie](releases/2.2.0-preview.1.md) en [oorspronkelijke voorstellen](feature-backlog-nl.md). De resterende beperkingen hierboven maken een claim dat alle 80 voorstellen volledig en op hardware bewezen zijn onjuist.
