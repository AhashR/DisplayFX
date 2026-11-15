# DisplayFX — design- en functionaliteitsbacklog

Datum: 15 november 2026. Gebaseerd op de bestaande WPF-app en de broncode in deze checkout. Dit zijn **voorstellen**, geen reeds toegevoegde functies. Een letterlijk volledige lijst van alle denkbare uitbreidingen bestaat niet; deze backlog bevat 80 concrete, voor DisplayFX relevante mogelijkheden.

De app heeft al profielen per scherm, gamma/contrast/kleurhelderheid, NVIDIA Digital Vibrance, DDC/CI-helderheid, sneltoetsen, applicatiekoppelingen, dagelijkse helderheidstijden, schermnamen, import/export, backups en logbestanden. Onderstaande punten bouwen daarop voort.

Prioriteit: **P1** eerst overwegen, **P2** waardevolle vervolgstap, **P3** optioneel/onderzoek. Omvang: **S** beperkt, **M** meerdere onderdelen, **L** grotere uitbreiding, **XL** onderzoek en/of nieuwe hardware-integratie. Dit zijn relatieve inschattingen, geen planningstoezeggingen.

## Design, bediening en toegankelijkheid — 20 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| D01 | Lichte modus en systeemthema | Kies donker, licht of Windows volgen. | P2 | M |
| D02 | Aanpasbare accentkleur | Geef selectie, sliders en focus een consistente eigen kleur. | P3 | S |
| D03 | Compacte en ruime weergave | Kies informatiedichtheid en grotere aanraakdoelen. | P2 | M |
| D04 | Schaalbare vensterindeling | Maak hoofdvenster resizable; pas kolommen aan beschikbare ruimte aan. | P1 | M |
| D05 | Duidelijkere hoofdacties | Optionele tekstlabels voor Toepassen, Opslaan en Herstellen. | P1 | S |
| D06 | Status met ernstniveau | Verschillende iconen/kleuren voor informatie, succes en fout; succes krijgt geen gevarendriehoek. | P1 | M |
| D07 | Bezig-indicator bij toepassen | Toon welk profiel wordt toegepast en maak wachtrijgedrag begrijpelijk. | P1 | M |
| D08 | Numerieke sliderinvoer | Typ exact een percentage of gammawaarde naast de slider. | P1 | M |
| D09 | Reset per kleurregelaar | Herstel één waarde zonder de rest van het profiel te veranderen. | P2 | S |
| D10 | Tooltips met uitleg | Leg GPU-kleurhelderheid, fysieke backlight, gamma en vibrance uit. | P1 | S |
| D11 | Profieliconen en kleurmarkeringen | Herken werk-, game-, film- en avondprofielen sneller. | P2 | M |
| D12 | Favorieten bovenaan | Pin veelgebruikte profielen en schermen. | P2 | M |
| D13 | Zoeken en filteren | Zoek profielen op naam, gekoppelde app of scherm. | P2 | M |
| D14 | Volledige toetsenbordroute | Voorspelbare Tab-volgorde, zichtbare focus en sneltoetsen voor hoofdacties. | P1 | M |
| D15 | Screenreader-informatie | Meld naam, eenheid, bereik, actieve status en het resultaat van toepassen. | P1 | M |
| D16 | Windows-contrastthema's | Gebruik systeemkleuren en houd status herkenbaar zonder kleur alleen. | P1 | M |
| D17 | Tekstschaal en lange teksten | Test 125–200% tekstschaal, lange namen en vertalingen zonder verborgen acties. | P1 | M |
| D18 | Minder animatie respecteren | Korte overgangen en ondersteuning voor verminderde beweging. | P2 | S |
| D19 | Taalkeuze | Nederlands en Engels met centrale vertaalresources. | P2 | L |
| D20 | Eerste-start-uitleg | Korte introductie met schermkeuze, testprofiel en uitleg over opslaan/toepassen. | P2 | M |

Microsoft beschrijft toetsenbordbediening, focus, contrast en schaalbaarheid als onderdelen van toegankelijke Windows-software: [accessibility checklist](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist), [Win32 accessibility best practices](https://learn.microsoft.com/en-us/windows/win32/winauto/accessibility-best-practices). De exacte WPF-uitwerking moet bij de bestaande resources passen.

## Profielen en kleuren — 12 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| P01 | Meer dan vijf profielen | Verhoog de huidige limiet met een scrollbare lijst en aangepaste validatie. | P1 | M |
| P02 | Profielen ordenen | Sleep of verplaats profielen; onthoud de volgorde. | P2 | M |
| P03 | Tijdelijke preview met terugvaltimer | Probeer kleuren en herstel automatisch tenzij je bevestigt. | P1 | L |
| P04 | Originele kleuren herstellen | Bewaar een sessiesnapshot van gamma/vibrance en herstel die op verzoek. | P1 | L |
| P05 | Voor/na vergelijken | Houd een knop ingedrukt om tijdelijk de uitgangswaarden te zien. | P2 | L |
| P06 | Meerstaps undo/redo | Ga verder terug dan alleen de laatste opgeslagen versie. | P2 | M |
| P07 | Kopiëren naar ander scherm | Kopieer kleuren met een expliciete waarschuwing dat schermen verschillen. | P1 | M |
| P08 | Sets voor meerdere schermen | Activeer één benoemde combinatie van profielen op meerdere schermen. | P2 | L |
| P09 | Eigen referentieprofiel | Kies welk profiel de terugval bij stoppen/verwijderen vormt. | P2 | M |
| P10 | Ingebouwde uitgangspunten | Lever conservatieve bewerkbare presets voor werk, film en games. | P3 | M |
| P11 | RGB-kanalen en kleurtemperatuur | Meer kleurregelaars waar de gekozen backend dit betrouwbaar ondersteunt. | P3 | XL |
| P12 | Profielnotities en wijzigingsdatum | Documenteer waarvoor een profiel is bedoeld en wanneer het veranderde. | P3 | S |

## Automatisering en games — 12 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| A01 | Automatisch schakelen pauzeren | Pauzeer tijdelijk, tot een tijdstip of tot de volgende herstart. | P1 | M |
| A02 | Meerdere apps per profiel | Koppel bijvoorbeeld launcher én game zonder extra kopieën van kleuren. | P1 | M |
| A03 | Regelprioriteiten | Maak conflicten tussen proces-, tijd- en handmatige regels voorspelbaar. | P1 | L |
| A04 | Instelbare schakeldrempel | Wacht kort op stabiele focus om flikkeren bij Alt-Tab te beperken. | P1 | M |
| A05 | Fullscreen-regels | Activeer alleen bij een expliciet ondersteund fullscreen-scenario. | P2 | L |
| A06 | Weekdagen en weekend | Voeg weekregels toe aan de huidige dagelijkse tijdstippen. | P2 | M |
| A07 | Rooster per scherm | Stel per monitor verschillende helderheidstijden in. | P1 | L |
| A08 | Geleidelijke overgangen | Laat helderheid/kleur rustig naar een doel bewegen met een instelbare duur. | P2 | L |
| A09 | Zonsopkomst/-ondergang | Gebruik handmatig gekozen plaats/tijden; locatiegebruik blijft optioneel. | P3 | L |
| A10 | Netvoeding en batterij | Schakel regels afhankelijk van stroomstatus waar relevant. | P3 | M |
| A11 | Wake- en dockbeleid | Kies expliciet wat er na hervatten of aansluiten opnieuw toegepast wordt. | P1 | L |
| A12 | Gebeurtenisgestuurde appdetectie | Onderzoek focus-events als aanvulling op de huidige periodieke controle. | P2 | L |

## Hardware en compatibiliteit — 10 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| H01 | Mogelijkheden per scherm | Toon welke kleur-/backlightfuncties beschikbaar zijn en waarom. | P1 | M |
| H02 | HDR-status en bescherming | Herken HDR en voorkom dat een onbetrouwbare gamma-route als ondersteund wordt gepresenteerd. | P1 | L |
| H03 | Driverwissel als expliciete herstelstatus | Toon verbinding herstellen, herdetecteer met begrensde retries en bied een concrete herstelactie. | P1 | L |
| H04 | Verwisselde/identieke schermen beheren | Laat de gebruiker onzekere identiteit handmatig bevestigen. | P2 | L |
| H05 | DDC/CI-mogelijkhedenscan | Toon ondersteunde VCP-functies met leesbare uitleg; schrijf alleen bij een expliciete actie. | P2 | L |
| H06 | Monitoringang wisselen | Voeg invoerselectie toe waar monitor en transport dit ondersteunen. | P3 | XL |
| H07 | Fysiek contrast en RGB-gain | Ondersteun aanvullende DDC-functies na verificatie per monitor. | P3 | XL |
| H08 | AMD-/Intel-kleurbackends | Onderzoek equivalente functies en presenteer alleen daadwerkelijk geteste mogelijkheden. | P3 | XL |
| H09 | Ingebouwde laptophelderheid | Voeg een afzonderlijke ondersteunde route voor interne panelen toe. | P2 | XL |
| H10 | Compatibiliteitsinformatie | Leg per verbinding/monitor vast wat getest werkt en wat beperkt is. | P2 | M |

HDR verdient een afzonderlijk ontwerp: Microsoft vermeldt dat `SetDeviceGammaRamp` ongedefinieerd gedrag heeft in HDR-modi en dat succes niet in alle gevallen betekent dat de gewenste ramp werd toegepast. Dit is dus meer dan een extra schuifregelaar: [SetDeviceGammaRamp](https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp). Digital Vibrance blijft een NVIDIA-functie; extra merken vereisen eigen onderzoek: [NVIDIA desktop color settings](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/Display/CS_Adjust_Color_Settings_Advanced.htm).

## Tray, helderheid en dagelijks gebruik — 8 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| T01 | Profielen direct vanuit de tray | Pas een profiel toe zonder het hoofdvenster te openen. | P1 | M |
| T02 | Benoemde helderheidsgroepen | Koppel alleen geselecteerde schermen, in meerdere groepen. | P2 | L |
| T03 | Offsets bij gekoppelde helderheid | Behoud bijvoorbeeld een verschil van 10 procentpunten tussen schermen. | P2 | M |
| T04 | Grenzen per scherm | Stel een praktische minimum-/maximumhelderheid in. | P2 | M |
| T05 | Doelscherm voor sneltoetsen | Kies alle schermen, geselecteerd scherm of scherm onder de muis. | P1 | M |
| T06 | Optionele schermmelding | Toon kort profielnaam en nieuwe helderheid na een sneltoets. | P2 | M |
| T07 | Popupvoorkeuren | Kies vastzetten, sluitgedrag, volgorde en informatievelden. | P3 | M |
| T08 | Afzonderlijk scherm uitschakelen | Bied aan/uit per ondersteund scherm met duidelijke herstelinstructies. | P3 | L |

## Diagnostiek, herstel en onderhoud — 8 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| R01 | Diagnostiekpaneel | Toon driverstatus, schermidentiteit, actieve backend en laatste fout. | P1 | M |
| R02 | Statusgeschiedenis | Bekijk welke actie slaagde/mislukte, met tijd en schermnaam. | P1 | M |
| R03 | Ondersteuningsrapport | Exporteer een lokaal rapport met preview en verwijderbare paden/persoonsgegevens. | P1 | M |
| R04 | Veilige zelftest | Test detectie en leesfuncties standaard; schrijfproeven alleen als aparte gebruikersactie. | P1 | L |
| R05 | Gerichte herstelknoppen | Herverbind één scherm of kleurbackend zonder alles opnieuw op te starten. | P1 | L |
| R06 | Externe kleurwijzigingen herkennen | Onderzoek detectie van Windows/andere apps die kleuren overschrijven, zonder strijd om instellingen. | P2 | XL |
| R07 | Instelbare opstartvertraging | Geef drivers/docks tijd voordat optionele profielen worden toegepast. | P2 | M |
| R08 | Update- en driverlogboek | Leg lokaal vast welke versiewissel voorafging aan nieuwe fouten. | P3 | M |

## Data, backups en integraties — 6 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| B01 | Importpreview | Toon toevoegingen, conflicten, capaciteit en doelmonitor vóór herstel. | P1 | L |
| B02 | Selectief importeren | Kies specifieke schermen, profielen of instellingen. | P1 | L |
| B03 | Automatische backups met versies | Bewaar een begrensd aantal herstelpunten naast de bestaande vorige-save-backup. | P1 | M |
| B04 | Geschiedenis vergelijken/herstellen | Bekijk verschillen en herstel één profiel uit een eerdere backup. | P2 | L |
| B05 | Portable modus | Kies expliciet opslag naast de app of per gebruiker; toon de actieve locatie. | P2 | L |
| B06 | Lokale CLI/API | Bedien benoemde profielen vanuit scripts/Stream Deck via een beperkte lokale interface. | P3 | XL |

## Distributie en productkwaliteit — 4 voorstellen

| ID | Voorstel | Concreet resultaat | Prio | Omvang |
|---|---|---|---|---|
| Q01 | In-app updatecontrole | Toon beschikbare versie en release notes; laat installatie expliciet starten. | P2 | L |
| Q02 | Ondertekende distributie | Onderteken executable/installer en verifieer herkomst van updates. | P1 | L |
| Q03 | Stabiel- en testkanaal | Maak experimentele builds herkenbaar en rollback eenvoudig. | P2 | L |
| Q04 | Hardwaretestmatrix in releaseproces | Volg NVIDIA/andere GPU's, HDR/SDR, docking, DPI, slaap en verschillende DDC-monitoren. | P1 | L |

## Aanbevolen volgorde

1. **Betrouwbaarheid zichtbaar maken:** D05–D07, H01–H03, R01–R05. De gebruiker moet weten wat daadwerkelijk is toegepast en wat herstel nodig heeft.
2. **Veilig en precies kleuren instellen:** D08–D10, P03–P04, P07. Terugval en exacte waarden verdienen voorrang op extra effecten.
3. **Dagelijks gemak:** T01, T05, P01, A01–A04, A07, B01–B03.
4. **Toegankelijkheid en polish:** D01, D04, D14–D19, Q02, Q04.
5. **Grotere uitbreidingen na compatibiliteitsonderzoek:** HDR-kleurpaden, RGB/temperatuur, nieuwe GPU-backends, interne panelen en externe bediening.

Nieuwe functies zijn niet automatisch onderdeel van de bugfixronde. Vooral HDR, extra GPU-backends en fysieke monitorcommando's moeten op echte hardware gevalideerd worden voordat de app ondersteuning belooft.
