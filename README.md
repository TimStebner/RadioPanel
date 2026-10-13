# MSFS Radio Panel

Kleines natives Windows-Fenster für COM1/COM2 Active und Standby in MSFS 2020 und 2024. Es verwendet Win32, GDI und die native SimConnect-C-API. Es gibt keine WebView- oder Chromium-Laufzeit.

## Bauen

Benötigt werden das .NET 10 SDK sowie die Windows Native-AOT-Voraussetzungen: MSVC C++ Build Tools und Windows SDK.

Im Projektordner:

    dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release
    dotnet publish .\src\RadioPanel\RadioPanel.csproj -c Release -r win-x64

Die Veröffentlichung ist auf Native AOT und eine win-x64-Einzeldatei konfiguriert. Die SimConnect-Client-DLL wird separat vom Simulator oder SDK bereitgestellt und nicht mitgeliefert.

## Laufzeitverhalten

- SimConnect-Verbindung und Win32-Nachrichtenschleife laufen auf demselben Thread. Bei fehlendem Simulator wird alle drei Sekunden erneut verbunden.
- Standard-SimVars liefern die universelle Datenquelle. Die vier Anfragen sind auf eine Sekunde und **SIMCONNECT_DATA_REQUEST_FLAG_CHANGED** begrenzt.
- Frequenzwerte werden als MHz/FLOAT64 angefordert und intern als ganzzahlige Hz gespeichert. Die MSFS-SimVar-Tabelle nennt COM-Frequenzen als Frequency BCD16; deshalb müssen die MHz-Anfragen in MSFS 2020 und 2024 live bestätigt werden.
- Das GDI-Fenster ist 320 × 160 px, rahmenlos und verwendet nur gezielte Invalidierungen. Frequenzänderungen zeichnen nur die betroffene Zeile; Statusänderungen zeichnen nur die LED. Es gibt keinen Render-Timer.
- Rechtsklick öffnet Always on Top und Exit. Das Fenster lässt sich an jeder Stelle mit der linken Maustaste ziehen. Position und Vordergrundstatus werden unter %LOCALAPPDATA%\RadioPanel\settings.ini gespeichert. Bei entfernten Monitoren wird die gespeicherte Position in einen sichtbaren Arbeitsbereich verschoben; ein neues Fenster startet bevorzugt auf einem zweiten Monitor.
- Ein Stern neben einer Fenix-Standby-Frequenz kennzeichnet einen Wert aus den Standard-SimVars, den die MobiFlight-Bridge nicht bestätigt hat.

## Flugzeugquellen und Grenzen

- **Standard:** COM1/COM2 SimVars, mit dem Fallback-Verhalten für alle Flugzeuge.
- **Fenix:** Der Client registriert sich zur Laufzeit über das dokumentierte MobiFlight-WASM-Client-Data-Protokoll und fordert die vier RMP-L-Variablen an. Wenn die Bridge nicht antwortet oder noch kein vollständiger gültiger Wertesatz vorliegt, bleiben die Standard-SimVars aktiv und Standby-Werte werden markiert. Die Skalierung und L-Variablen müssen mit installiertem Fenix und MobiFlight live geprüft werden.
- **PMDG:** Der bereitgestellte 777-SDK-Header definiert die Client-Data-Area `PMDG_777X_Data`, aber die Datenstruktur enthält keine COM-Aktiv-/Standbyfrequenzfelder. Daher bleibt der PMDG-Radioprovider unverfügbar und der Router verwendet Standard-SimVars; es werden keine Offsets geraten. Der 737-SDK-Header wurde nicht geprüft.

Das Arbeitsverzeichnis enthält weder MSFS noch eine SimConnect-Client-DLL. Eine echte MSFS-Integration sowie RAM-/CPU-Messungen sind deshalb noch nicht bestätigt.

## Tests

Das MSTest-Projekt prüft native Fenstermaße, SimConnect-Verbindung und Wiederverbindung, Frequenzkonvertierung, SimVar- und MobiFlight-Routing, Dirty-Repaint-Regionen, native GDI-Ausgabe, INI-Persistenz und Monitor-Wiederherstellung.

Für die MobiFlight-Bridge siehe [MobiFlight WASM Module](https://github.com/MobiFlight/MobiFlight-WASM-Module); für den Client-Data-Zeitraum siehe Microsofts [SIMCONNECT_CLIENT_DATA_PERIOD](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_CLIENT_DATA_PERIOD.htm).
