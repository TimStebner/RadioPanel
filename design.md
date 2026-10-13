# MSFS Radio Panel – Architekturentwurf

**Datum:** 2026-10-04  
**Status:** Zur Prüfung durch den Nutzer  
**Umfang:** Architektur und erste Ausbaustufe (Projektbasis und SimConnect-Verbindung). Noch kein Produktcode.

## Ziel

Ein natives Windows-Panel für MSFS 2020 und MSFS 2024 zeigt ausschließlich diese vier Werte an:

1. COM1 Active
2. COM1 Standby
3. COM2 Active
4. COM2 Standby

Das Fenster ist kompakt (320 × 160 px bei 96 DPI), dunkel, rahmenlos, frei verschiebbar und optional immer im Vordergrund. Es zeigt außerdem eine kleine Verbindungs-LED. Frequenzen werden nur neu gezeichnet, wenn ihr Wert wechselt. Es gibt keine dauerhafte Render-Schleife und keinen Web-Stack.

## Technologieentscheid

**C# Native AOT mit einer direkt über Win32/GDI gezeichneten Oberfläche.** Das Projekt zielt auf net10.0-windows und win-x64, veröffentlicht als Native-AOT-Einzeldatei und verwendet keine NuGet-Laufzeitabhängigkeiten. WinForms wird ausgeschlossen, weil Microsoft es als nicht Native-AOT-kompatibel einstuft. .NET 10 ist am Stichtag die aktive LTS-Version; .NET 8 und 9 erreichen ihr Supportende am 10. November 2026.

SimConnect wird über die native C-API angebunden, nicht über den älteren verwalteten Wrapper. Damit bleibt die Verbindungsschicht AOT-freundlich und das Fenster benötigt kein UI-Framework. Native AOT enthält weiterhin Laufzeitkomponenten; die Speichergrenze ist daher ein Abnahmekriterium, das am veröffentlichten Build gemessen werden muss.

## Projektstruktur

    outputs/RadioPanel/
    ├── RadioPanel.sln
    ├── src/RadioPanel/
    │   ├── RadioPanel.csproj
    │   ├── Program.cs
    │   ├── Win32/
    │   │   ├── NativeMethods.cs
    │   │   └── PanelWindow.cs
    │   └── SimConnect/
    │       ├── SimConnectNative.cs
    │       └── SimConnectConnection.cs
    └── README.md

Program.cs registriert das Fenster und startet die blockierende Windows-Nachrichtenschleife. NativeMethods.cs enthält nur benötigte Win32-Aufrufe. PanelWindow.cs verarbeitet Fensterereignisse und zeichnet per GDI. SimConnectNative.cs kapselt die nativen SimConnect-Einstiegspunkte und ABI-Datentypen. SimConnectConnection.cs besitzt Handle, Zustand und Wiederverbindungslogik.

## SimConnect-Verbindung

Die Verbindungsinstanz wird an das Panel-Fenster gebunden. Alle SimConnect-Aufrufe und Empfangsverarbeitung laufen auf demselben UI-Thread, da SimConnect-Clients laut SDK nicht thread-sicher sind.

### Zustände und Ablauf

- **Waiting:** SimConnect ist nicht erreichbar; die LED ist gelb.
- **Connecting:** SimConnect_Open wurde aufgerufen, die Öffnungsbestätigung steht noch aus.
- **Connected:** SIMCONNECT_RECV_OPEN wurde empfangen; die LED ist grün.
- **Error:** Ein Clientbibliotheks-, Versions- oder API-Fehler liegt vor; die LED ist rot. Der Verbindungsversuch wird weiterhin alle drei Sekunden wiederholt.

Start(hwnd) beginnt mit einem Verbindungsversuch. SimConnect_Open erhält das Fensterhandle und eine eigene Windows-Nachricht (WM_APP + n). Diese Nachricht veranlasst das Leeren der SimConnect-Empfangswarteschlange. Ein Versuch ohne SIMCONNECT_RECV_OPEN wird nach drei Sekunden geschlossen und erneut gestartet. Nach bestätigter Verbindung wird der Retry-Timer beendet. Bei QUIT, Verbindungsabbruch oder fatalem Fehler wird das Handle geschlossen; nach drei Sekunden beginnt ein neuer Versuch. Beim Beenden wird SimConnect_Close genau einmal aufgerufen.

Ein Win32-Timer wird nur im getrennten oder noch nicht bestätigten Zustand verwendet. Im verbundenen Leerlauf blockiert die Nachrichtenschleife; es gibt weder einen Polling-Thread noch einen Render-Timer.

### Erste Frequenzanforderung (Folgeschritt)

Die Standardquelle definiert die vier SimVars COM ACTIVE FREQUENCY und COM STANDBY FREQUENCY mit den COM-Indizes 1 und 2 und fordert sie mit der Einheit `Hz` und `FLOAT64` an. Obwohl das SDK für diese SimVars standardmäßig Frequency BCD16 angibt, darf `SimConnect_AddToDataDefinition` eine andere Einheit derselben Einheitenfamilie anfordern. `Hz` erhält die für 8.33-kHz-Kanäle nötige Auflösung; BCD16 allein kann 8.33-kHz-Werte nicht vollständig unterscheiden. Der Parser unterstützt für weitere Datenkanäle BCD16, BCD32 und Hz und formatiert gültige Werte auf drei Dezimalstellen, einschließlich 118.005, 122.800 und 136.990. Er berücksichtigt 8.33-kHz- und 25-kHz-Kanalwerte.

Die Datenanforderung verwendet SIMCONNECT_DATA_REQUEST_FLAG_CHANGED und zunächst SIMCONNECT_PERIOD_SECOND. Das begrenzt die Auswertung auf höchstens einmal pro Sekunde und sendet keine unveränderten Daten an den Client. Ein Unterschied an einer SimVar liefert die vollständige Definition zurück. Die UI fordert daraufhin nur bei einem tatsächlichen Wert- oder Statuswechsel eine Neuzeichnung an. Windows darf zusätzlich selbst WM_PAINT auslösen, etwa nach dem Freilegen des Fensters; es gibt keine anwendungsseitige periodische Neuzeichnung.

## Spätere Datenquellen und Aircraft Router

Die Standard-SimVars bleiben universeller Fallback. Ein späterer Router fragt nach Sitzungsstart und AircraftLoaded-Ereignissen TITLE (maximal 256 Byte) und ATC MODEL einmalig ab; String-SimVars werden wegen der SimConnect-Einschränkung mit SIMCONNECT_PERIOD_ONCE angefordert. PMDG wird bei Titelmerkmalen PMDG, 737 oder 777 versucht; Fenix bei Fenix oder FNX. Die Existenz einer Hersteller-Data-Area beziehungsweise Bridge wird geprüft, bevor deren Werte verwendet werden.

- Titel mit PMDG-/737-/777-Merkmalen lösen einen Versuch über eine für das konkrete Produkt verifizierte PMDG-Quelle aus. Der bereitgestellte 777-SDK-Header deklariert die Client-Data-Area `PMDG_777X_Data`, enthält in `PMDG_777X_Data` jedoch keine COM-Aktiv-/Standbyfrequenzfelder. Der 777 nutzt daher Standard-SimVars; der PMDG-737-Header ist noch nicht geprüft. Es werden keine nicht belegten Offsets verwendet.
- Titel mit Fenix-/FNX-Merkmalen lösen eine Abfrage der MobiFlight-WASM-Bridge für die L-Variablen N_PED_RMP1_ACTIVE, N_PED_RMP1_STDBY, N_PED_RMP2_ACTIVE und N_PED_RMP2_STDBY aus. Ist die Bridge nicht verfügbar oder liefert sie keinen gültigen Wert, gelten Standard-SimVars als Fallback. Ein nicht bestätigter Standby-Wert wird diskret markiert und protokolliert.
- Andere Flugzeuge verwenden die Standard-SimVars.

Die Source-Adapter werden getrennt von der Verbindungsklasse gehalten. So bleibt der SimConnect-Lebenszyklus unabhängig von PMDG-, Fenix- und Standarddaten.

## Oberfläche und Persistenz

GDI zeichnet auf dunklem Hintergrund. Aktive Frequenzen erscheinen in Bernstein (#FFB000) oder Weiß, Standby in Cyan (#00E5FF) oder gedämpftem Grau. Die Frequenzdarstellung verwendet eine gut lesbare Monospace-Schrift. Die kleine LED zeigt den SimConnect-Zustand.

Das rahmenlose Fenster lässt sich an seiner Fläche verschieben. Ein Rechtsklick öffnet ein kleines natives Menü für „Always on Top“ und „Beenden“. Position und Top-most-Zustand werden in einer schlanken INI-Datei unter %LOCALAPPDATA%\RadioPanel gespeichert. Liegt die gespeicherte Position nach einem Monitorwechsel außerhalb des sichtbaren Desktopbereichs, wird das Fenster auf einen sichtbaren Bereich zurückgesetzt.

## Leistungs- und Funktionsabnahme

Die folgenden Werte sind Zielgrenzen für den veröffentlichten x64-Build; sie sind erst nach Messung bestätigt:

- **RAM:** höchstens 20.000.000 Byte Private Working Set, 60 Sekunden nach Verbindungsaufbau und Leerlauf.
- **CPU:** im verbundenen Leerlauf 0,0 % Anzeige über 60 Sekunden; im getrennten Zustand nur ein Retry-Versuch alle drei Sekunden.
- **Rendering:** keine dauerhafte Schleife; app-seitige Invalidierung nur bei geänderten Frequenzen oder Status.
- **Wiederverbindung:** bei verfügbarer SimConnect-Clientbibliothek höchstens ein 3-Sekunden-Retry-Intervall.
- **Funktion:** vier Frequenzen stimmen bei MSFS 2020 und 2024; Fensterposition und Top-most-Zustand bleiben nach Neustart erhalten.
- **Deployment:** AOT-Build erzeugt eine native EXE-Einzeldatei. Die SimConnect-Clientbibliothek kann eine zusätzliche native Laufzeitabhängigkeit bleiben; ihr Such- und Verteilungspfad muss vor der finalen Paketierung festgelegt werden. Der Build-Rechner benötigt außerdem die Native-AOT-Werkzeugkette für Windows.

Die vom Nutzer genannte Obergrenze „15–20 MB“ wird für diesen Entwurf als maximal 20.000.000 Byte Private Working Set interpretiert. Falls eine andere Windows-Speichermetrik gemeint ist, sollte das bei der Spezifikationsprüfung angepasst werden.

## Umsetzungsschnitt

1. Projektgrundlage und Win32-Nachrichtenschleife mit Native-AOT-Konfiguration.
2. SimConnect-P/Invoke, Zustandsmaschine, Fensterbenachrichtigung und 3-Sekunden-Reconnect.
3. BCD16-/BCD32-/Hz-Parser samt den angeforderten Unit-Tests.
4. GDI-Anzeige, Dirty-Invalidierung, Status-LED und Fensterbedienung.
5. Aircraft Router sowie PMDG-/Fenix-/Standard-Provider.

Die vorliegende Freigabe deckt die Architektur ab. Die konkrete SimConnect-DLL-Lade- und Verteilungsstrategie sowie Messwerte für RAM/CPU werden vor der jeweiligen Paketierungsentscheidung festgelegt.

## Offizielle Referenzen

- [Microsoft: Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/index)
- [Microsoft: .NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Microsoft Flight Simulator: SimConnect SDK und 2020/2024-Kompatibilität](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/simconnect-sdk/)
- [Microsoft Flight Simulator: SimConnect_Open](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/general/simconnect_open/)
- [Microsoft Flight Simulator: SimConnect_RequestDataOnSimObject](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Events_And_Data/SimConnect_RequestDataOnSimObject.htm)
- [Microsoft Flight Simulator: SimConnect_AddToDataDefinition](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_AddToDataDefinition.htm)
- [Microsoft Flight Simulator: Simulation Variable Units](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/simulation-variable-units/)
- [Microsoft Flight Simulator: COM-Radio-SimVars](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_RadioNavigation_Variables.htm)


