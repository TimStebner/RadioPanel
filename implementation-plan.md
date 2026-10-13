# MSFS Radio Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Ein nativer Windows-Prozess zeigt COM1/COM2 Active und Standby für MSFS 2020 und 2024 an und verbindet sich nach einem Simulatorstart selbstständig erneut.

**Architecture:** Eine Win32-Nachrichtenschleife besitzt Fenster, Timer und SimConnect-Handle auf demselben Thread. Standard-SimVars liefern den universellen Fallback; ein Aircraft-Router wählt PMDG- oder Fenix-Daten, wenn die jeweilige Quelle verfügbar und gültig ist. GDI zeichnet nur geänderte Frequenzzeilen oder den Verbindungsstatus.

**Tech Stack:** C#, .NET 10 Windows, Native AOT, Win32, GDI, native SimConnect-C-API; MSTest nur im Testprojekt.

**Spec:** [design.md](design.md)

## Global Constraints

- Zielplattform ist win-x64 mit net10.0-windows, PublishAot=true und einer nativen Einzeldatei.
- Keine WinForms-, Electron-, Tauri-, WebView-, Chromium- oder NuGet-Laufzeitabhängigkeit.
- Rahmenloses Fenster, 320 × 160 px bei 96 DPI; nur vier Frequenzen und eine dezente Verbindungsanzeige.
- SimConnect-Aufrufe und Empfang laufen auf dem Fensterthread; bei getrennter oder unbestätigter Verbindung alle 3 Sekunden erneut versuchen.
- Frequenzdaten mit SIMCONNECT_DATA_REQUEST_FLAG_CHANGED und SIMCONNECT_PERIOD_SECOND anfordern; kein verbundener Polling- oder Render-Timer.
- Standard-COM-SimVars als MHz/FLOAT64 anfordern, damit drei Dezimalstellen angezeigt werden können. Die MSFS-Dokumentation listet COM-Frequenzen als Frequency BCD16; die MHz-Konvertierung muss live in MSFS 2020/2024 geprüft werden.
- Frequenz- und Statusänderungen lösen gezielte Invalidierungen aus; kein anwendungsseitiger Dauer-Render.
- Private Working Set nach 60 Sekunden verbundenem Leerlauf höchstens 20.000.000 Byte; Task-Manager-CPU im gleichen Zeitraum 0,0 %.
- Fensterposition und Always-on-Top-Zustand unter %LOCALAPPDATA%/RadioPanel/settings.ini speichern und bei nicht mehr sichtbarer Position wiederherstellen.
- Das aktuelle Arbeitsverzeichnis ist kein Git-Repository; dieser Plan enthält daher keine Commit-Schritte und legt keines an.

## Review Focus

- SimConnect-Client-DLL fehlt, der Simulator läuft noch nicht oder SIMCONNECT_RECV_OPEN bleibt aus: Status korrekt anzeigen und Retry fortsetzen (Task 2: **ConnectionTests.RetryAfterOpenFailureAndHandshakeTimeout**).
- BCD-Daten enthalten ungültige Nibbles, Hz-Werte sind NaN/nicht ganzzahlig oder Frequenzen liegen außerhalb 118.000–136.990 MHz: ungültigen Wert nicht als Frequenz anzeigen (Task 3: **FrequencyFormatterTests.RejectMalformedAndOutOfRangeValues**).
- Ein Flugzeug stellt COM2 oder einen einzelnen Frequenzwert nicht bereit: fehlenden Wert als ---.--- rendern, ohne gültige COM1-Werte zu verwerfen (Task 4: **StandardRadioProviderTests.PreservesAvailableValuesWhenOneFieldIsMissing**).
- PMDG-/Fenix-Quelle fehlt, ist ungültig oder wird beim Flugzeugwechsel nicht neu initialisiert: Standardwerte übernehmen und keine alten Flugzeugwerte anzeigen (Task 5: **AircraftRouterTests.FallsBackAndClearsPreviousAircraftValues**).
- Gespeicherte Position liegt nach Monitorentfernung außerhalb aller Arbeitsbereiche: Fenster vollständig auf einen sichtbaren Monitor setzen (Task 7: **SettingsTests.ClampsOffscreenWindowToVisibleWorkArea**).

---

## Projektdateien und Zuständigkeiten

    outputs/RadioPanel/
    ├── RadioPanel.sln
    ├── src/RadioPanel/
    │   ├── RadioPanel.csproj
    │   ├── Program.cs
    │   ├── Domain/
    │   │   ├── AircraftIdentity.cs
    │   │   ├── ConnectionStatus.cs
    │   │   └── RadioFrequencies.cs
    │   ├── Platform/
    │   │   ├── NativeMethods.cs
    │   │   ├── MonitorPlacement.cs
    │   │   └── Win32ReconnectTimer.cs
    │   ├── SimConnect/
    │   │   ├── ISimConnectApi.cs
    │   │   ├── SimConnectNative.cs
    │   │   ├── SimConnectMessage.cs
    │   │   ├── SimConnectConnection.cs
    │   │   ├── FrequencyFormatter.cs
    │   │   ├── StandardRadioProvider.cs
    │   │   └── AircraftIdentityProvider.cs
    │   ├── Aircraft/
    │   │   ├── IRadioProvider.cs
    │   │   ├── AircraftRouter.cs
    │   │   ├── PmdgRadioProvider.cs
    │   │   └── FenixRadioProvider.cs
    │   ├── Settings/
    │   │   ├── PanelSettings.cs
    │   │   └── IniStore.cs
    │   └── UI/
    │       ├── PanelWindow.cs
    │       ├── GdiRenderer.cs
    │       └── DirtyRegionCalculator.cs
    ├── tests/RadioPanel.Tests/
    │   ├── RadioPanel.Tests.csproj
    │   ├── ConnectionTests.cs
    │   ├── WindowHostTests.cs
    │   ├── FrequencyFormatterTests.cs
    │   ├── StandardRadioProviderTests.cs
    │   ├── AircraftRouterTests.cs
    │   ├── DirtyRegionCalculatorTests.cs
    │   └── SettingsTests.cs
    └── README.md

## Task 1: Scaffold the Native AOT Win32 executable

**Files:**
- Create: RadioPanel.sln
- Create: src/RadioPanel/RadioPanel.csproj
- Create: src/RadioPanel/Program.cs
- Create: src/RadioPanel/Platform/NativeMethods.cs
- Create: src/RadioPanel/UI/PanelWindow.cs
- Create: tests/RadioPanel.Tests/RadioPanel.Tests.csproj
- Create: tests/RadioPanel.Tests/WindowHostTests.cs
- Create: README.md

**Interfaces:**
- Program.Main() -> int starts PanelWindow.Run().
- PanelWindow.Run() -> int registers the window class, creates one 320 × 160 window, and runs GetMessage/TranslateMessage/DispatchMessage until WM_QUIT.
- PanelWindow.CreateNativeWindow() -> nint creates the frameless window and returns its HWND; used by Run() and by the Windows host smoke test.
- NativeMethods exposes only the Win32 functions, structures, and constants used by the window and message loop; native callbacks must remain compatible with Native AOT.

- [x] Create the solution, executable project and Windows MSTest project. Configure net10.0-windows, win-x64 publishing, PublishAot=true, PublishSingleFile=true and AllowUnsafeBlocks only where native interop requires it.
- [x] Write WindowHostTests.CreatesFrameless320By160Window using reflection to locate PanelWindow.CreateNativeWindow(), then assert the returned HWND is valid, its rectangle is 320 × 160, and WS_CAPTION/WS_THICKFRAME/WS_BORDER are unset.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~WindowHostTests**.
  Expected: the test compiles and fails its missing-window assertion because PanelWindow.CreateNativeWindow() is not implemented.
- [x] Implement the minimal frameless native window and blocking message loop; show a dark client area and close cleanly on WM_CLOSE.
- [x] Run the WindowHostTests filter again.
  Expected: the test passes and destroys its test HWND in a finally block.
- [x] Document build prerequisites and the initial publish command in README.md.
- [ ] Run **dotnet publish .\src\RadioPanel\RadioPanel.csproj -c Release -r win-x64**.
  Expected: publish succeeds and creates the native executable under src/RadioPanel/bin/Release/net10.0-windows/win-x64/publish/.

## Task 2: Implement SimConnect interop and reconnect lifecycle

**Files:**
- Create: src/RadioPanel/Domain/ConnectionStatus.cs
- Create: src/RadioPanel/Platform/Win32ReconnectTimer.cs
- Create: src/RadioPanel/SimConnect/ISimConnectApi.cs
- Create: src/RadioPanel/SimConnect/SimConnectNative.cs
- Create: src/RadioPanel/SimConnect/SimConnectMessage.cs
- Create: src/RadioPanel/SimConnect/SimConnectConnection.cs
- Modify: src/RadioPanel/UI/PanelWindow.cs
- Test: tests/RadioPanel.Tests/ConnectionTests.cs

**Interfaces:**
- ConnectionStatus = Waiting | Connecting | Connected | Error.
- ISimConnectApi.Open(nint hwnd, uint windowMessage, out nint handle) -> int; Close(nint handle) -> int; GetNextDispatch(nint handle, out SimConnectMessage message) -> int.
- IReconnectTimer.Start(nint hwnd, uint timerId, uint intervalMs) and Stop(nint hwnd, uint timerId); Win32ReconnectTimer implements it with SetTimer/KillTimer.
- SimConnectMessage is an immutable value containing receive ID, request ID, definition ID, exception code and a copied payload byte array.
- SimConnectConnection(ISimConnectApi api, IReconnectTimer retryTimer) exposes Status and Start(nint hwnd), HandleSimConnectWindowMessage(), HandleRetryTimer(), Stop(); events StateChanged(ConnectionStatus) and MessageReceived(SimConnectMessage).
- PanelWindow routes the private WM_APP message and WM_TIMER to SimConnectConnection; no worker thread is introduced.

- [x] Write ConnectionTests.RetryAfterOpenFailureAndHandshakeTimeout plus tests for successful open awaiting SIMCONNECT_RECV_OPEN, QUIT/retry, missing client DLL, fatal dispatch, and Stop closing the handle exactly once.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~ConnectionTests**.
  Expected: the new lifecycle tests fail because the connection implementation is absent.
- [x] Implement source-generated or otherwise Native-AOT-safe P/Invoke declarations for SimConnect_Open, SimConnect_Close and SimConnect_GetNextDispatch. Convert only the receive header needed for dispatch routing and copy the variable payload before returning from the native callback.
- [x] Implement the four-state lifecycle. A failed Open HRESULT is treated as Waiting and retried; a missing/incompatible client library or fatal dispatch/API error is Error. Retry every 3000 ms, close timed-out handles before retry, stop the timer after OPEN, drain dispatch until S_FALSE or SIMCONNECT_RECV_ID_NULL, and close once on shutdown.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~ConnectionTests**.
  Expected: all ConnectionTests pass; repeated WM_TIMER messages never create a second live handle.

## Task 3: Add immutable radio values and tested frequency conversion

**Files:**
- Create: src/RadioPanel/Domain/RadioFrequencies.cs
- Create: src/RadioPanel/SimConnect/FrequencyFormatter.cs
- Test: tests/RadioPanel.Tests/FrequencyFormatterTests.cs

**Interfaces:**
- RadioFrequencies is an immutable record struct containing nullable integer Hz values for COM1 active/standby and COM2 active/standby plus flags identifying unverified standby values.
- FrequencyFormatter.TryDecodeBcd16(ushort encoded, out int frequencyHz) -> bool.
- FrequencyFormatter.TryDecodeBcd32(uint encoded, out int frequencyHz) -> bool.
- FrequencyFormatter.TryDecodeHertz(double encoded, out int frequencyHz) -> bool.
- FrequencyFormatter.Format(int frequencyHz) -> string; successful results use invariant culture and exactly three decimals.

- [x] Write tests for BCD16 0x1800 -> 118.000, 0x2280 -> 122.800 and 0x3699 -> 136.990; BCD32 0x118005 -> 118.005, 0x118025 -> 118.025, 0x122800 -> 122.800 and 0x136990 -> 136.990; Hz 118005000 -> 118.005, 118025000 -> 118.025, 122800000 -> 122.800 and 136990000 -> 136.990.
- [x] Write FrequencyFormatterTests.RejectMalformedAndOutOfRangeValues for invalid BCD nibbles, fractional/NaN Hz, values below 118000000 Hz and above 136990000 Hz.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~FrequencyFormatterTests**.
  Expected: the new conversion tests fail before implementation.
- [x] Implement packed BCD validation and conversion to integer Hz; BCD16 is limited to its representable channel precision, while BCD32 and Hz preserve three-decimal channels.
- [x] Implement Format using integer arithmetic and invariant culture; do not round malformed inputs into a plausible frequency.
- [x] Run the FrequencyFormatterTests filter again.
  Expected: all valid examples format exactly as specified and invalid values return false.

## Task 4: Subscribe to standard COM SimVars and aircraft identity

**Files:**
- Create: src/RadioPanel/SimConnect/StandardRadioProvider.cs
- Create: src/RadioPanel/SimConnect/AircraftIdentityProvider.cs
- Create: src/RadioPanel/Domain/AircraftIdentity.cs
- Modify: src/RadioPanel/SimConnect/ISimConnectApi.cs
- Modify: src/RadioPanel/SimConnect/SimConnectNative.cs
- Modify: src/RadioPanel/SimConnect/SimConnectConnection.cs
- Test: tests/RadioPanel.Tests/StandardRadioProviderTests.cs

**Interfaces:**
- StandardRadioProvider.Start() registers one data definition with four fields: COM ACTIVE FREQUENCY:1, COM STANDBY FREQUENCY:1, COM ACTIVE FREQUENCY:2, COM STANDBY FREQUENCY:2. Each field requests MHz/FLOAT64 and index 1 or 2.
- StandardRadioProvider exposes IsAvailable, Current, Start(nint simConnectHandle), Stop(), OnSimConnectMessage(in SimConnectMessage message) and ValuesChanged(RadioFrequencies); it raises the event only when at least one decoded value changes.
- AircraftIdentity is an immutable record struct with Title and AtcModel strings.
- AircraftIdentityProvider exposes Start(nint simConnectHandle), Stop() and OnSimConnectMessage(in SimConnectMessage); it subscribes to AircraftLoaded and requests TITLE as STRING256 and ATC MODEL as STRING128 once on connection open and each AircraftLoaded, then emits IdentityChanged(AircraftIdentity).
- Extend ISimConnectApi with the data definition, request-data and system-event calls required by these providers.

- [x] Write provider tests asserting the four exact variable names, indexes, MHz units, FLOAT64 type, SIMCONNECT_PERIOD_SECOND and SIMCONNECT_DATA_REQUEST_FLAG_CHANGED.
- [x] Write StandardRadioProviderTests.PreservesAvailableValuesWhenOneFieldIsMissing and StandardRadioProviderTests.DoesNotRaiseChangeForIdenticalValues, plus aircraft-identity registration and AircraftLoaded refresh coverage.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~StandardRadioProviderTests**.
  Expected: the tests fail before provider registration and decoding exist.
- [x] Implement standard requests at one-second changed-only cadence. Decode MHz through FrequencyFormatter and represent absent or invalid fields as null without dropping the remaining valid values.
- [x] Implement one-shot TITLE/ATC MODEL requests at session start and after AircraftLoaded. Reset identity on aircraft change; the router will reset standard radio display state when it consumes the identity event.
- [x] Run the StandardRadioProviderTests filter again.
  Expected: the definition/request contract and change-only behavior pass.

## Task 5: Route aircraft-specific providers with safe fallback

**Files:**
- Create: src/RadioPanel/Aircraft/IRadioProvider.cs
- Create: src/RadioPanel/Aircraft/AircraftRouter.cs
- Create: src/RadioPanel/Aircraft/PmdgRadioProvider.cs
- Create: src/RadioPanel/Aircraft/FenixRadioProvider.cs
- Modify: src/RadioPanel/Domain/RadioFrequencies.cs
- Modify: src/RadioPanel/SimConnect/StandardRadioProvider.cs
- Modify: src/RadioPanel/SimConnect/SimConnectNative.cs
- Modify: src/RadioPanel/SimConnect/SimConnectConnection.cs
- Test: tests/RadioPanel.Tests/AircraftRouterTests.cs

**Interfaces:**
- IRadioProvider exposes IsAvailable, Current, Start(nint simConnectHandle), Stop(), ResetForAircraftChange(), OnSimConnectMessage(in SimConnectMessage message) and event ValuesChanged(RadioFrequencies).
- AircraftRouter.Classify(AircraftIdentity identity) -> AircraftFamily uses case-insensitive title matching: PMDG/737/777 first, Fenix/FNX second, otherwise Standard.
- AircraftRouter selects valid special-provider values when available and otherwise selects StandardRadioProvider values. A provider becomes available only after receiving a complete, valid initial sample for its aircraft.
- PmdgRadioProvider remains unavailable until a product/version-specific SDK layout exposes all four COM active/standby frequency fields. The supplied 777 header declares `PMDG_777X_Data` but does not expose those fields; the PMDG 737 header is unverified. Unsupported/missing radio fields use standard SimVars.
- FenixRadioProvider requests N_PED_RMP1_ACTIVE, N_PED_RMP1_STDBY, N_PED_RMP2_ACTIVE and N_PED_RMP2_STDBY through the published MobiFlight WASM Client Data protocol. Missing bridge or invalid values report unavailable; fallback standby values carry an unverified flag.

- [x] Write AircraftRouterTests.FallsBackAndClearsPreviousAircraftValues plus tests for case-insensitive classification, standard aircraft, PMDG/Fenix provider success, absent providers and malformed partial provider data.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~AircraftRouterTests**.
  Expected: routing and fallback tests fail before the router and provider adapters are implemented.
- [x] Inspect the supplied official PMDG 777 SDK header and verify the client-area name `PMDG_777X_Data`; its data structure has no COM active/standby frequency fields. The PMDG 737 header remains unverified, so no offsets are inferred.
- [ ] Implement PMDG client-data registration and map active/standby fields only if an SDK for the exact product exposes them. The supplied 777 layout has no such fields; keep its provider unavailable and use standard SimVars.
- [x] Implement MobiFlight Client Data registration using the protocol's published area/command names, request the four named L-Var values, and accept only a complete valid response.
- [x] Implement router source priority and aircraft-change reset. Keep standard SimVars active as fallback; for Fenix mark any fallback standby values unverified in the UI.
- [x] Run the AircraftRouterTests filter again.
  Expected: missing or malformed special sources use standard values, and values from the previous aircraft are cleared.

## Task 6: Draw the compact panel with dirty invalidation

**Files:**
- Create: src/RadioPanel/UI/GdiRenderer.cs
- Create: src/RadioPanel/UI/DirtyRegionCalculator.cs
- Modify: src/RadioPanel/UI/PanelWindow.cs
- Modify: src/RadioPanel/Domain/RadioFrequencies.cs
- Test: tests/RadioPanel.Tests/DirtyRegionCalculatorTests.cs

**Interfaces:**
- DirtyRegionCalculator.GetDirtyRegions(RadioFrequencies previous, RadioFrequencies current, ConnectionStatus previousStatus, ConnectionStatus currentStatus) -> IReadOnlyList<Rectangle>.
- GdiRenderer.Draw(nint hdc, Rectangle clip, RadioFrequencies values, ConnectionStatus status) draws the initial surface or only requested dirty rectangles.
- PanelWindow invalidates the first complete client area at creation; after that it invalidates only changed frequency rows, changed Fenix standby indicators, or the connection LED rectangle.

- [x] Write tests asserting that equal snapshots yield no dirty rectangles, a single frequency change invalidates only that row, and a status-only change invalidates only the LED.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~DirtyRegionCalculatorTests**.
  Expected: dirty-region tests fail before the calculator exists.
- [x] Implement a 320 × 160 dark GDI panel with labeled COM1/COM2 active and standby rows, monospaced text, #FFB000 active, #00E5FF standby, and a small green/yellow/red connection LED.
- [x] Render null values as ---.--- and show a subtle marker only beside Fenix standby values sourced from the standard fallback.
- [x] Route WM_PAINT through BeginPaint/EndPaint and remove any continuous redraw timer. Repaint from the window message loop only after the calculator reports a visual change.
- [x] Run the DirtyRegionCalculatorTests filter and inspect the native HWND/GDI output with a pixel smoke test.
  Expected: all calculator tests pass; the panel shows only the four frequencies and status LED, with no periodic invalidation.

## Task 7: Add native window controls and INI persistence

**Files:**
- Create: src/RadioPanel/Settings/PanelSettings.cs
- Create: src/RadioPanel/Settings/IniStore.cs
- Create: src/RadioPanel/Platform/MonitorPlacement.cs
- Modify: src/RadioPanel/Platform/NativeMethods.cs
- Modify: src/RadioPanel/UI/PanelWindow.cs
- Test: tests/RadioPanel.Tests/SettingsTests.cs

**Interfaces:**
- PanelSettings is an immutable record struct with X, Y and AlwaysOnTop.
- IniStore.Load(string path, PanelSettings fallback) -> PanelSettings and Save(string path, PanelSettings settings) -> void use %LOCALAPPDATA%/RadioPanel/settings.ini.
- MonitorPlacement.ClampToWorkArea(Rectangle window, IReadOnlyList<Rectangle> workAreas) -> Rectangle preserves the window size and returns a fully visible position, preferring the monitor with the most overlap or nearest center.

- [x] Write SettingsTests.ClampsOffscreenWindowToVisibleWorkArea plus tests for valid INI round-trip and malformed/missing values using safe defaults.
- [x] Run **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release --filter FullyQualifiedName~SettingsTests**.
  Expected: settings and placement tests fail before implementation.
- [x] Implement INI load/save and monitor work-area clamping. Save the latest location on close and the topmost setting when changed.
- [x] Add WM_NCHITTEST-based dragging and a right-click native menu with Always on Top toggle and Exit. Apply the topmost state using SetWindowPos.
- [x] Run the SettingsTests filter again, including live native monitor enumeration.
  Expected: all settings and monitor-placement tests pass.

## Task 8: Publish, integrate and verify resource limits

**Files:**
- Modify: README.md
- Review: all files created above

**Interfaces:**
- No new runtime interface. README.md documents build, SimConnect client-library prerequisite, supported source behavior, fallback indicators, controls and manual acceptance results.

- [x] Run the full automated suite: **dotnet test .\tests\RadioPanel.Tests\RadioPanel.Tests.csproj -c Release**.
  Expected: all parser, connection, provider, routing, dirty-region and settings tests pass.
- [ ] Publish the final application: **dotnet publish .\src\RadioPanel\RadioPanel.csproj -c Release -r win-x64**.
  Expected: Native AOT publish succeeds without unexplained trimming/AOT warnings and produces one application EXE; document the required native SimConnect client library separately.
- [ ] Verify with MSFS 2020 and MSFS 2024: connect after simulator start, display all four values, change each frequency, close/reopen the simulator, and confirm automatic reconnect within one retry interval.
- [ ] Verify PMDG 737/777 against the installed SDK layouts and Fenix with and without the MobiFlight bridge; confirm standard fallback and the subtle unverified-standby marker when Fenix standby is not confirmed.
- [ ] Verify Always on Top, dragging, saved position, restart persistence and monitor-removal recovery.
- [ ] Measure the published process after 60 seconds connected and idle: Private Working Set <= 20,000,000 bytes; Task Manager CPU display = 0.0%; confirm there is no periodic repaint while values and status remain unchanged.
- [ ] Record simulator versions, aircraft/provider availability and measured RAM/CPU in README.md. If a target is exceeded, optimize and repeat the measurement before calling the build accepted.

