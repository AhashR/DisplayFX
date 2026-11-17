# Implementatiestatus van de 80 voorstellen

Build: **2.2.14**. Deze lijst beschrijft de implementatie in deze checkout. De app is nog niet volledig op echte hardware gekwalificeerd. Een geslaagde gesimuleerde test bewijst geen werking van een monitorfirmware, HDR-driver of laptop.

**Toegevoegd** betekent dat de bediening, opslag en uitvoeringsroute aanwezig zijn. **Hardware** betekent dat de route is toegevoegd, maar fysieke schrijfproeven en de relevante hardwarematrix nog nodig zijn. **Deels** vermeldt een concrete resterende beperking. **Voorbereid** vereist een extern middel of releasebewijs.

**Verwijderd** betekent dat het oorspronkelijke voorstel niet meer beschikbaar is in deze build. De matrix volgt alle 80 oorspronkelijke voorstellen.

Uitgebreide instellingen staan onder **Workspace** in de bovenste werkbalk. De kleurschuifregelaars staan in het hoofdvenster. Bestaande instellingen blijven via het tandwiel bereikbaar.

| ID | Status | Uitwerking en toegang |
|---|---|---|
| D01 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| D02 | Verwijderd | Niet meer beschikbaar in deze build. |
| D03 | Verwijderd | Niet meer beschikbaar in deze build. |
| D04 | Toegevoegd | Schaalbaar hoofdvenster, flexibele editor en scrollbare lijsten. |
| D05 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| D06 | Toegevoegd | Icoon met ernstniveau en volledige tekst in de tooltip, zonder aparte statusregel. |
| D07 | Toegevoegd | De knop toont Applying tijdens een handmatige toepassing, zonder knipperende voortgangsbalk. |
| D08 | Toegevoegd | Klik op de waarde naast een kleurregelaar om exact te typen. |
| D09 | Verwijderd | Losse resetknoppen zijn weg. Revert voor de bewerkte instellingen blijft. |
| D10 | Verwijderd | Niet meer beschikbaar in deze build. |
| D11 | Toegevoegd | Kies per profiel één van tien vaste iconen. Een aparte markeerkleur is verwijderd. |
| D12 | Verwijderd | Niet meer beschikbaar in deze build. |
| D13 | Verwijderd | Niet meer beschikbaar in deze build. |
| D14 | Verwijderd | Niet meer beschikbaar in deze build. |
| D15 | Verwijderd | Niet meer beschikbaar in deze build. |
| D16 | Toegevoegd | Systeemkleuren bij Windows-contrastthema; ernst ook met iconen/tekst. Fysieke contrastthematest nog nodig. |
| D17 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| D18 | Verwijderd | Niet meer beschikbaar in deze build. |
| D19 | Verwijderd | Niet meer beschikbaar in deze build. |
| D20 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| P01 | Toegevoegd | Maximaal 100 profielen per monitor; import hanteert dezelfde grens. |
| P02 | Toegevoegd | Profielcontextmenu: omhoog/omlaag, blijvend opgeslagen volgorde. |
| P03 | Verwijderd | Niet meer beschikbaar in deze build. |
| P04 | Verwijderd | De afzonderlijke knop om de oorspronkelijke sessiekleuren te herstellen is weg. |
| P05 | Verwijderd | Niet meer beschikbaar in deze build. |
| P06 | Verwijderd | De undo-/redo-knoppen van de kleureneditor zijn weg. Revert blijft beschikbaar. |
| P07 | Verwijderd | Kopiëren van kleuren naar een ander scherm is uit het profielmenu verwijderd. |
| P08 | Verwijderd | Profielsets zijn niet langer beschikbaar. |
| P09 | Deels | Eerder ingestelde referentieprofielen blijven werken als terugval, maar zijn niet meer instelbaar via het profielmenu. |
| P10 | Verwijderd | Niet meer beschikbaar in deze build. |
| P11 | Verwijderd | Niet meer beschikbaar in deze build. |
| P12 | Deels | Icoonkeuze blijft. Notities, favorieten en wijzigingsdatum zijn niet meer zichtbaar in het profielmenu. |
| A01 | Verwijderd | Niet meer beschikbaar in deze build. |
| A02 | Verwijderd | Niet meer beschikbaar in deze build. |
| A03 | Verwijderd | Niet meer beschikbaar in deze build. |
| A04 | Verwijderd | Niet meer beschikbaar in deze build. |
| A05 | Verwijderd | Niet meer beschikbaar in deze build. |
| A06 | Verwijderd | Niet meer beschikbaar in deze build. |
| A07 | Verwijderd | Niet meer beschikbaar in deze build. |
| A08 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| A09 | Verwijderd | Niet meer beschikbaar in deze build. |
| A10 | Verwijderd | Niet meer beschikbaar in deze build. |
| A11 | Verwijderd | Niet meer beschikbaar in deze build. |
| A12 | Toegevoegd | Foreground WinEvent-hook met polling als terugval. |
| H01 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| H02 | Hardware | Actieve advanced color/HDR/WCG blokkeert legacy gamma. Op deze pc alleen SDR waargenomen. |
| H03 | Hardware | NVIDIA herdetectie, verouderde handles opruimen, begrensde herstelpogingen en verversactie. Live driververvanging niet gereproduceerd. |
| H04 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| H05 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| H06 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| H07 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| H08 | Deels | AMD ADLX/Intel IGCL onderzocht en beperking zichtbaar gemaakt. Extra AMD-/Intel-saturatiebackends ontbreken. Alleen deze NVIDIA-pc beschikbaar; Windows-gamma blijft afhankelijk van compatibele drivers. Zie vendor-color-backends.md. |
| H09 | Hardware | Afzonderlijke WMI-route met exacte panelinstance, begrensde cache en transportinvalidatie; alleen injecteerbare fake getest. |
| H10 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| T01 | Hardware | Profielenmenu direct vanuit de tray. |
| T02 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| T03 | Verwijderd | Niet meer beschikbaar in deze build. |
| T04 | Verwijderd | Niet meer beschikbaar in deze build. |
| T05 | Verwijderd | Niet meer beschikbaar in deze build. |
| T06 | Verwijderd | Niet meer beschikbaar in deze build. |
| T07 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| T08 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R01 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R02 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R03 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R04 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R05 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R06 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R07 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| R08 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B01 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B02 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B03 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B04 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B05 | Verwijderd | Niet beschikbaar in de vereenvoudigde app. |
| B06 | Toegevoegd | Meegeleverde CLI: list en apply via beperkte named pipe voor dezelfde Windows-gebruiker/sessie. Protocol getest; live serverinteractie nog handmatig kwalificeren. |
| Q01 | Toegevoegd | Expliciete updatecontrole bij publieke GitHub-releases met release-noteslink. Geen automatische installatie. |
| Q02 | Voorbereid | Certificaatselectie, timestamp, Authenticode-verificatie en verplichte ondertekening voor stabiele publicatie. Geen certificaat beschikbaar; de lokale build kan ongetekend zijn. |
| Q03 | Verwijderd | Niet meer beschikbaar in deze build. |
| Q04 | Voorbereid | Hardwarematrix plus vereiste releasebewijsvalidator. Werkelijke matrix is nog niet afgerond. |

## Verificatie en vindplaatsen

Regressies staan in `tests/DisplayFX.RegressionTests`, inclusief `FeatureExpansionRegressionChecks`, bestaande fout-/concurrentietests en losse WPF-renders. Thema's en tekstschaal staan in `AppearanceController`; uitgebreide bediening in `Interface/Workspace`; kleurherstel en profielsetlogica in de partials van `Interface/Shell`; transporten in `MonitorHardwareFeatures`/`InternalPanelBrightness`.

Op deze NVIDIA-pc zijn alleen uitleesproeven uitgevoerd: twee SDR-uitgangen, leesbare NVIDIA-vibrance en helderheid. De Dell-uitgang adverteert backlight, contrast, RGB, invoer en power. Er zijn bij deze controle geen fysieke instellingen geschreven, geen monitoren uitgeschakeld en geen instellingen van de geïnstalleerde app vervangen.

Zie [hardwaretestmatrix](hardware-test-matrix.md), [release-informatie](releases/2.2.4.md) en [oorspronkelijke voorstellen](feature-backlog-nl.md). De resterende beperkingen hierboven maken een claim dat alle 80 voorstellen volledig en op hardware bewezen zijn onjuist.
