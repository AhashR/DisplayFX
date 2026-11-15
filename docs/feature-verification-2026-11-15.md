# Verificatie van 2.2.0-preview.1

Datum: 15 november 2026. Dit zijn uitgevoerde controles, geen volledige hardwarekwalificatie.

## Geslaagd

- Releasebuild van de oplossing, inclusief app, vendored displaybibliotheek, regressieharnas en CLI.
- **657 hardware-onafhankelijke regressiecontroles**. Dit omvat opslag/migratie, importcapaciteit, profielbewerking, appregels, sneltoetsconflicten, brightness-transportsimulaties, races en herstel, previewannulering/terugval, gedeeltelijk mislukte profielsets, week-/zonregels, VCP-validatie, WMI-instancekoppeling, pipeframing en losse WPF-vensters.
- WPF-renders van hoofdvenster, helderheidspopup, bestaande instellingen en alle zeven beheertabbladen. Licht/Nederlands/200% apart gerenderd en bekeken. Dit is geen interactieve test van de geïnstalleerde app.
- Read-only hardwarediagnostiek: DISPLAY3 en DISPLAY4 melden advanced color uit, helderheid respectievelijk 15% en 100%, NVIDIA-vibrance leesbaar. DISPLAY4 adverteert backlight, contrast, RGB-gain, invoerkeuze en power. Er zijn geen hardwarewaarden geschreven.
- CLI geeft voor ongeldige argumenten de gebruikstekst en exitcode 2.
- Vier PowerShell-scripts zonder syntaxfouten. Verplichte signing weigert een ontbrekend certificaat; de hardwarebewijsvalidator weigert ontbrekend releasebewijs.
- De implementatiematrix bevat alle 80 unieke voorstel-ID's. `git diff --check` slaagt.
- Installatiescript bouwt app en CLI in een nieuwe stagingmap en weigert Data, Logs, History, portable.flag en Runtime.json in de distributie.

## Waarschuwingen en grenzen

- NuGet meldt NU1900 omdat de online kwetsbaarheidsdatabase niet bereikbaar is. De build slaagt met lokaal aanwezige dependencies; er is geen actuele online dependency-audit uitgevoerd.
- Inno Setup meldt de bestaande combinatie van admin-installatie en HKCU-opruiming. Installeren/upgraden/verwijderen onder een ander administratoraccount is nog niet gekwalificeerd.
- Geen codesigning-certificaat beschikbaar. De lokale preview is ongetekend; signing is voorbereid zoals gevraagd.
- Geen AMD-/Intel-pc of intern laptoppaneel beschikbaar. Native ADLX-/IGCL-saturatiebackends ontbreken; de nieuwe WMI-route is met een fake getest.
- Geen fysieke schrijfproeven voor gamma/vibrance, DDC/VCP, power, input, HDR, slaap/docking of gemengde DPI. Een terugval kan op verdwenen/defecte hardware zelf mislukken.
- Geen volledige interactieve toetsenbord-/screenreaderacceptatie en geen live CLI-serveracceptatietest. Sommige technische fout-/rapportmeldingen zijn nog Engels.
- Een nieuwe GPU-driver is niet tijdens deze proef vervangen. De herstelroute heeft automatische controles, maar de oorspronkelijke storing is niet opnieuw op echte hardware opgewekt.

De [implementatiematrix](feature-implementation-status.md) geeft de status per voorstel. Zie [hardwaretestmatrix](hardware-test-matrix.md) voor resterende kwalificatie. De lokale buildlog, regressie-uitvoer, hardware-uitlezing en renders staan in `artifacts/features/`.
