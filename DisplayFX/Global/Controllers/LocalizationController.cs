using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows;

namespace DisplayFX.Global.Controllers;

public sealed class LocalizationController : INotifyPropertyChanged
{
    public static LocalizationController Instance { get; } = new();
    public string Language { get; private set; } = "en";
    public static string WelcomeText => Instance.Language == "nl"
        ? "1. Selecteer een scherm en een profiel.\n2. Pas kleuren aan en kies Toepassen voor een preview. Opslaan bewaart de wijzigingen.\n3. Fysieke schermhelderheid regel je afzonderlijk via het systeemvak.\n4. Gebruik een tijdelijke preview om automatisch naar de vorige kleuren terug te keren.\n5. Beheer bevat weergave, automatisering, backups en diagnostiek.\n\nHardwarefuncties zijn afhankelijk van je scherm en driver. HDR vereist een geschikte kleurroute."
        : "1. Select a monitor and a profile.\n2. Adjust colors, then Apply to preview. Save stores your edits.\n3. Physical backlight brightness is separate: open the tray popup.\n4. Use a timed preview to automatically restore your previous colors.\n5. Workspace contains appearance, automation, backups and diagnostics.\n\nHardware controls depend on the display and driver. HDR requires a compatible color route.";
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly Dictionary<string, string> _resourceEnglish = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Dutch = new(StringComparer.Ordinal)
    {
        ["Saved profile → selected backup"] = "Opgeslagen profiel → geselecteerde backup",
        ["New profile; no current profile on this display to compare."] = "Nieuw profiel; geen huidig profiel op dit scherm om mee te vergelijken.",
        ["No saved color or rule differences."] = "Geen verschillen in opgeslagen kleuren of regels.",
        ["Additional application paths"] = "Extra applicatiepaden", ["Temperature tint"] = "Temperatuurtint",
        ["Input and power commands can disconnect this display. To restore it, use the monitor's physical input selector or power button; software power-on may be unavailable."] = "Invoer- en powercommando's kunnen dit scherm loskoppelen. Herstel via de fysieke invoerkeuze of aan/uitknop van de monitor; softwarematig inschakelen is mogelijk niet beschikbaar.",
        ["Profiles"] = "Profielen", ["Color brightness"] = "Kleurhelderheid", ["Contrast"] = "Contrast",
        ["Vibrance"] = "Kleurintensiteit", ["Gamma"] = "Gamma", ["Settings"] = "Instellingen",
        ["Apply"] = "Toepassen", ["Save"] = "Opslaan", ["Revert"] = "Herstellen", ["Undo"] = "Ongedaan maken", ["Redo"] = "Opnieuw",
        ["Unsaved changes"] = "Niet-opgeslagen wijzigingen", ["New profile"] = "Nieuw profiel", ["Import profile colors"] = "Profielkleuren importeren",
        ["Identify displays"] = "Schermen identificeren", ["Refresh displays"] = "Schermen verversen",
        ["General"] = "Algemeen", ["Launch with Windows"] = "Starten met Windows", ["Start minimised"] = "Geminimaliseerd starten",
        ["Close to tray"] = "Sluiten naar systeemvak", ["Restore colour profiles"] = "Kleurprofielen herstellen", ["Restore brightness"] = "Helderheid herstellen",
        ["Color change diagnostics"] = "Diagnostiek van kleurwijzigingen", ["Open diagnostic logs"] = "Diagnoselogboeken openen",
        ["Settings backup"] = "Instellingenbackup", ["Back up"] = "Backup maken", ["Restore"] = "Herstellen",
        ["Brightness shortcuts"] = "Helderheidssneltoetsen", ["Daily brightness"] = "Dagelijkse helderheid",
        ["Time"] = "Tijd", ["Brightness (%)"] = "Helderheid (%)", ["Done"] = "Klaar", ["Step (%)"] = "Stap (%)",
        ["Profile shortcut"] = "Profielsneltoets", ["Record profile shortcut"] = "Profielsneltoets opnemen", ["Clear shortcut"] = "Sneltoets wissen",
        ["Linked application"] = "Gekoppelde applicatie", ["Link application"] = "Applicatie koppelen", ["Unlink application"] = "Applicatie ontkoppelen",
        ["Search profiles"] = "Profielen zoeken", ["Workspace"] = "Beheer", ["Preview (15 seconds)"] = "Preview (15 seconden)", ["Keep preview"] = "Preview behouden",
        ["Restore original colors"] = "Originele kleuren herstellen", ["Pause automation"] = "Automatisering pauzeren", ["Resume automation"] = "Automatisering hervatten",
        ["Appearance"] = "Weergave", ["Automation"] = "Automatisering", ["Hardware"] = "Hardware", ["Backups"] = "Backups",
        ["Diagnostics"] = "Diagnostiek", ["Updates"] = "Updates", ["Theme"] = "Thema", ["Language"] = "Taal",
        ["Accent (#RRGGBB)"] = "Accent (#RRGGBB)", ["Density"] = "Dichtheid", ["Show action labels"] = "Actietekst tonen",
        ["Reduce motion"] = "Beweging beperken", ["Apply preferences"] = "Voorkeuren toepassen", ["Search"] = "Zoeken",
        ["Notes"] = "Notities", ["Favorite"] = "Favoriet", ["Move up"] = "Omhoog", ["Move down"] = "Omlaag",
        ["Copy to monitor"] = "Kopiëren naar scherm", ["Set as reference"] = "Als referentie instellen", ["Create preset"] = "Preset maken",
        ["Save metadata"] = "Details opslaan", ["Name"] = "Naam", ["Test"] = "Test", ["Export report"] = "Rapport exporteren",
        ["Read-only self-test"] = "Zelftest zonder wijzigingen", ["Reconnect selected display"] = "Geselecteerd scherm herverbinden",
        ["Check for updates"] = "Op updates controleren", ["Open release page"] = "Releasepagina openen", ["Open logs"] = "Logboeken openen",
        ["Profile notes"] = "Profielnotities", ["More"] = "Meer", ["Help"] = "Hulp", ["Reset"] = "Reset",
        ["Brightness"] = "Helderheid", ["Link brightness controls"] = "Helderheidsregelaars koppelen", ["Turn off displays"] = "Schermen uitschakelen",
        ["Open main app"] = "Hoofdvenster openen", ["Open DisplayFX"] = "DisplayFX openen",
        ["Apply profile / preview edits"] = "Profiel toepassen / wijzigingen bekijken", ["Revert edits"] = "Wijzigingen herstellen", ["Save profile"] = "Profiel opslaan",
        ["Full screen only"] = "Alleen volledig scherm", ["Power condition"] = "Voedingsvoorwaarde", ["Rule priority"] = "Regelprioriteit",
        ["Additional application paths (one per line)"] = "Extra applicatiepaden (één per regel)",
        ["Profile sets"] = "Profielsets", ["Capture active set"] = "Actieve set vastleggen", ["Apply set"] = "Set toepassen",
        ["Show welcome guide"] = "Welkomstuitleg tonen", ["Pin brightness popup"] = "Helderheidspopup vastzetten",
        ["Show hardware details"] = "Hardwaredetails tonen", ["Versioned backups"] = "Backups met versies",
        ["Create snapshot"] = "Herstelpunt maken", ["Open backup folder"] = "Backupmap openen",
        ["Compare selected backup"] = "Geselecteerde backup vergelijken", ["Restore selected profile"] = "Geselecteerd profiel herstellen"
        , ["24-hour time (HH:mm)"] = "24-uurs tijd (UU:mm)", ["Active profile"] = "Actief profiel", ["Add scheduled time"] = "Tijdstip toevoegen", ["Add time"] = "Tijd toevoegen",
        ["Adjusts the GPU color curve. Physical backlight brightness is controlled separately from the tray."] = "Past de GPU-kleurcurve aan. Fysieke schermhelderheid regel je afzonderlijk via het systeemvak.",
        ["Also import application preferences"] = "Ook appvoorkeuren importeren", ["Apply profile"] = "Profiel toepassen",
        ["Apply selected import / restore profile"] = "Selectie importeren / profiel herstellen", ["Apply the active saved profile on launch"] = "Pas bij het starten het actieve opgeslagen profiel toe",
        ["Back up saved profiles and settings"] = "Maak een backup van opgeslagen profielen en instellingen", ["Back up settings"] = "Instellingen back-uppen",
        ["Brightness %"] = "Helderheid %", ["Brightness automation status"] = "Status helderheidsautomatisering", ["Brightness from 0 to 100"] = "Helderheid van 0 tot 100",
        ["Brightness group"] = "Helderheidsgroep", ["Brightness shortcut status"] = "Status helderheidssneltoets", ["Brightness shortcut target"] = "Doelscherm voor helderheidssneltoetsen",
        ["Brightness step percent"] = "Helderheidsstap in procenten", ["Brightness transition (milliseconds, 0–30000)"] = "Helderheidsovergang (milliseconden, 0–30000)",
        ["Built With:"] = "Gemaakt met:", ["Cancel"] = "Annuleren", ["Cancel preview"] = "Preview annuleren", ["Choose backup for selective import"] = "Backup kiezen voor selectieve import",
        ["Color brightness percent"] = "Kleurhelderheid in procenten", ["Compatibility notes"] = "Compatibiliteitsnotities", ["Confirm saved display match"] = "Schermkoppeling bevestigen",
        ["Contrast %"] = "Contrast %", ["Contrast percent"] = "Contrast in procenten",
        ["Contrast themes override application colors. Keyboard: Ctrl+S saves, Ctrl+Enter applies, Ctrl+Z/Shift+Ctrl+Z undo/redo color edits. All controls retain standard Tab navigation."] = "Windows-contrastthema's hebben voorrang. Ctrl+S slaat op, Ctrl+Enter past toe en Ctrl+Z/Ctrl+Shift+Z maakt kleurwijzigingen ongedaan of opnieuw. Navigeer met Tab.",
        ["Copy saved colors"] = "Opgeslagen kleuren kopiëren", ["Custom monitor name"] = "Eigen schermnaam", ["Daily / weekly brightness schedules"] = "Dagelijkse / wekelijkse helderheid",
        ["Daily time"] = "Dagelijks tijdstip", ["Days bitmask (Sun=1…Sat=64)"] = "Dagenmasker (zo=1…za=64)", ["Decrease brightness"] = "Helderheid verlagen",
        ["Decrease brightness shortcut"] = "Sneltoets voor lagere helderheid", ["Detect external gamma changes (observe only)"] = "Externe gammawijzigingen detecteren (alleen observeren)",
        ["Device path (empty = all)"] = "Apparaatpad (leeg = alle)", ["DisplayFX Workspace"] = "DisplayFX Beheer", ["Enable automatic profile switching"] = "Automatisch profielen wisselen",
        ["Enable brightness schedules"] = "Helderheidsroosters inschakelen", ["Exact values / reset"] = "Exacte waarden / reset", ["Favorite monitor"] = "Favoriet scherm",
        ["Fixed / Sunrise / Sunset"] = "Fixed / Sunrise / Sunset", ["Gamma value"] = "Gammawaarde", ["Help & About"] = "Hulp en informatie",
        ["Hold mouse or Space to show original session colors. Release to return."] = "Houd de muisknop of spatie ingedrukt voor de oorspronkelijke sessiekleuren. Laat los om terug te keren.",
        ["Hold to compare"] = "Ingedrukt houden om te vergelijken", ["Icon (up to 8 characters)"] = "Icoon (maximaal 8 tekens)", ["Import"] = "Importeren",
        ["Include device paths in report"] = "Apparaatpaden in rapport opnemen", ["Include monitor names in report"] = "Schermnamen in rapport opnemen",
        ["Increase brightness"] = "Helderheid verhogen", ["Increase brightness shortcut"] = "Sneltoets voor hogere helderheid", ["Latitude"] = "Breedtegraad",
        ["Linked offset (%)"] = "Verschil bij koppeling (%)", ["Local time; after sleep, the latest due level is applied"] = "Lokale tijd; na slaapstand geldt het laatst verstreken niveau", ["Longitude"] = "Lengtegraad",
        ["Manually match a disconnected saved panel to the selected connected display"] = "Koppel een opgeslagen niet-aangesloten scherm handmatig aan het geselecteerde aangesloten scherm",
        ["Marker color (#RRGGBB)"] = "Markeringskleur (#RRGGBB)", ["Minimum / maximum brightness (%)"] = "Minimale / maximale helderheid (%)", ["Monitor"] = "Scherm",
        ["Next scheduled times"] = "Volgende geplande tijden", ["Next times"] = "Volgende tijden", ["Offset minutes"] = "Verschuiving in minuten",
        ["Only advertised DDC/CI commands are listed. Values are monitor-specific; input or power changes can disconnect the display. Power-on may require a physical button when firmware stops listening."] = "Alleen gemelde DDC/CI-functies zijn zichtbaar. Waarden verschillen per monitor. Ingangs- of stroomwijzigingen kunnen de verbinding verbreken. Soms is de fysieke aan/uitknop nodig om te ontwaken.",
        ["Optional solar location: enter both latitude and longitude manually. No location service is used."] = "Optionele locatie voor zonnestanden: voer zelf breedte- en lengtegraad in. Er wordt geen locatiedienst gebruikt.",
        ["Pause / resume until restart"] = "Pauzeren / hervatten tot herstart", ["Pause automatic profiles (minutes)"] = "Automatische profielen pauzeren (minuten)",
        ["Prepare portable mode (restart required)"] = "Portable modus voorbereiden (herstart nodig)", ["Preview merge"] = "Samenvoeging bekijken",
        ["Previous scheduled times"] = "Vorige geplande tijden", ["Previous times"] = "Vorige tijden", ["Profile"] = "Profiel", ["Profile name"] = "Profielnaam",
        ["Read brightness around color changes to investigate unexpected monitor behavior. No brightness corrections are sent."] = "Lees helderheid rond kleurwijzigingen om onverwacht schermgedrag te onderzoeken. Er worden geen helderheidscorrecties gestuurd.",
        ["Read-only capability scan"] = "Mogelijkheden uitlezen", ["Reapply colors after docking or mode changes"] = "Kleuren opnieuw toepassen na aansluiten of moduswijziging", ["Reapply colors after wake"] = "Kleuren opnieuw toepassen na slaapstand",
        ["Record decrease shortcut"] = "Sneltoets voor verlagen opnemen", ["Record increase shortcut"] = "Sneltoets voor verhogen opnemen", ["Remove scheduled time"] = "Gepland tijdstip verwijderen",
        ["Remove selected time"] = "Geselecteerd tijdstip verwijderen", ["Remove time"] = "Tijdstip verwijderen", ["Rename monitor"] = "Scherm hernoemen",
        ["Reset all data. Restarts application."] = "Alle gegevens resetten. Herstart de app.", ["Restore a DisplayFX settings backup"] = "Een DisplayFX-instellingenbackup herstellen", ["Restore settings"] = "Instellingen herstellen",
        ["Restore the last successful brightness on launch"] = "Herstel bij het starten de laatste geslaagde helderheid", ["Retain snapshots (1–100)"] = "Aantal herstelpunten (1–100)",
        ["Right-click to give this monitor a custom name."] = "Klik rechts om dit scherm een eigen naam te geven.", ["Save brightness schedules"] = "Helderheidsroosters opslaan", ["Save brightness settings"] = "Helderheidsinstellingen opslaan",
        ["Save brightness shortcuts and schedule"] = "Helderheidssneltoetsen en rooster opslaan", ["Save compatibility notes"] = "Compatibiliteitsnotities opslaan", ["Save monitor settings"] = "Scherminstellingen opslaan",
        ["Save name"] = "Naam opslaan", ["Scheduled brightness percent"] = "Geplande helderheid in procenten", ["Send selected monitor command"] = "Geselecteerde monitoropdracht sturen",
        ["Show change notifications"] = "Wijzigingsmeldingen tonen", ["Stable focus delay (milliseconds, 0–10000)"] = "Vertraging voor stabiele focus (milliseconden, 0–10000)",
        ["Startup delay (seconds, 0–120)"] = "Opstartvertraging (seconden, 0–120)", ["The close button keeps DisplayFX running in the tray"] = "De sluitknop laat DisplayFX in het systeemvak draaien",
        ["Update channel"] = "Updatekanaal", ["Update checks contact the public DisplayFX GitHub repository only when requested. Installation is always started by you. Local test builds are unsigned until a publisher certificate is configured."] = "Updatecontrole maakt alleen op verzoek verbinding met de openbare DisplayFX-repository op GitHub. Je start de installatie zelf. Lokale testbuilds zijn onondertekend zolang er geen uitgeverscertificaat is ingesteld.",
        ["Use hardware name"] = "Hardware-naam gebruiken", ["Use selected monitor for shortcuts"] = "Geselecteerd scherm voor sneltoetsen gebruiken", ["Vibrance %"] = "Kleurintensiteit %", ["Vibrance percent"] = "Kleurintensiteit in procenten"
        , ["System"] = "Systeem", ["Dark"] = "Donker", ["Light"] = "Licht", ["Compact"] = "Compact", ["Comfortable"] = "Ruim", ["Any"] = "Altijd", ["AC"] = "Netvoeding", ["Battery"] = "Accu",
        ["All"] = "Alle schermen", ["Selected"] = "Geselecteerd", ["Pointer"] = "Onder de muis", ["Stable"] = "Stabiel", ["Work"] = "Werk", ["Film"] = "Film", ["Evening"] = "Avond", ["Gaming"] = "Games",
        ["All displays"] = "Alle schermen", ["Fixed time"] = "Vast tijdstip", ["Sunrise"] = "Zonsopkomst", ["Sunset"] = "Zonsondergang", ["Sun"] = "zo", ["Mon"] = "ma", ["Tue"] = "di", ["Wed"] = "wo", ["Thu"] = "do", ["Fri"] = "vr", ["Sat"] = "za",
        ["Text scale (100–200%)"] = "Tekstschaal (100–200%)", ["Time source"] = "Tijdbron", ["RGB / temperature tint"] = "RGB / kleurtemperatuurtint",
        ["Per-channel GPU curves; temperature is an approximate tint, not a calibrated white point. Unavailable in HDR/advanced color mode."] = "GPU-curves per kleurkanaal. De temperatuur is een benaderde tint, geen gekalibreerd witpunt. Niet beschikbaar bij HDR/geavanceerde kleurmodus.",
        ["Red gain"] = "Roodversterking", ["Green gain"] = "Groenversterking", ["Blue gain"] = "Blauwversterking", ["Approximate temperature tint"] = "Benaderde kleurtemperatuurtint"
        , ["Fixed"] = "Vast tijdstip", ["Saved"] = "Opgeslagen volgorde", ["Favorites"] = "Favorieten", ["Brightness popup order"] = "Volgorde in helderheidspopup",
        ["Prepare per-user mode (restart required)"] = "Opslag per gebruiker voorbereiden (herstart nodig)", ["Reference profile"] = "Referentieprofiel",
        ["Resume at local time (HH:mm)"] = "Hervatten op lokale tijd (UU:mm)", ["Pause until time"] = "Pauzeren tot tijdstip", ["Color transition (milliseconds, 0–5000)"] = "Kleurovergang (milliseconden, 0–5000)",
        ["Applying colors…"] = "Kleuren worden toegepast…", ["Welcome to DisplayFX"] = "Welkom bij DisplayFX", ["Inactive profile"] = "Inactief profiel",
        ["Favorite / unfavorite"] = "Favoriet aan/uit", ["Duplicate"] = "Dupliceren", ["Export profile..."] = "Profiel exporteren...", ["Rename..."] = "Hernoemen...", ["Remove"] = "Verwijderen",
        ["Adjust brightness"] = "Helderheid aanpassen", ["Show menu"] = "Hoofdvenster openen", ["Exit"] = "Afsluiten", ["Rename profile"] = "Profiel hernoemen", ["Duplicate profile"] = "Profiel dupliceren",
        ["Enter a profile name."] = "Voer een profielnaam in.", ["Use 80 characters or fewer."] = "Gebruik maximaal 80 tekens.", ["The name cannot contain control characters."] = "De naam mag geen besturingstekens bevatten.",
        ["A profile with this name already exists on this monitor."] = "Op dit scherm bestaat al een profiel met deze naam.", ["Click to record shortcut"] = "Klik om een sneltoets op te nemen", ["Press key combination..."] = "Druk een toetsencombinatie in..."
    };
    public string this[string key] => Language == "nl" && Dutch.TryGetValue(key, out var translated) ? translated : key;
    public void SetLanguage(string language)
    {
        Language = language;
        if (Application.Current is { } app)
        {
            CollectStrings(app.Resources);
            foreach (var entry in _resourceEnglish) app.Resources[entry.Key] = this[entry.Value];
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
    private void CollectStrings(ResourceDictionary dictionary)
    {
        foreach (var child in dictionary.MergedDictionaries) CollectStrings(child);
        foreach (var key in dictionary.Keys)
            if (key is string name && name.StartsWith("UiText", StringComparison.Ordinal) && dictionary[key] is string text) _resourceEnglish.TryAdd(name, text);
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;
    public string Key { get; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding("[" + Key + "]") { Source = LocalizationController.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
