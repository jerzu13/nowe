# AC Coach – coach for Assetto Corsa (C#, .NET Framework 4.8, WinForms)

Code: `ACCoach/zrodla/src`. User: Polish, chat in Polish. App: English by default, Polish optional.
History, parked topics, open items: `NOTATKI.md` – read it before bigger changes.

## Build & test (Linux, Mono)
- `ACCoach/zrodla/build.sh` (exe + test tools), `build.sh release` (+ installer + ZIP in `dist/`)
- `ACCoach/zrodla/test.sh` – smoke test ~4.5 min: AcSim session through the real window, offline lap order (`AcSim --order`), report EN/PL, map in Chromium
- Run the exe under Mono with `tools/test/bin/System.Speech.dll` next to it (stub; see BUILD_LINUX.md)
- No Windows/game here: shared memory, voice and real driving are untested – say so to the user

## Map of the code
- `LiveCoach*.cs` – live trainer (partial class): loop/laps (`LiveCoach.cs`), `.Voice` (corner calls), `.Beeps`
  (countdown, reaction lead), `.Focus`, `.Margins`, `.Rival`, `.Target` (lap time target), `.Controls` (live API from UI)
- `MainForm*.cs` – the one window: tabs Drive/Analysis/Settings+Help, `LogWriter` = Console → log box + `<data>\log.txt`
- `Analyzer.cs` report, `TrackMap.cs` + `map_template.html` HTML map, `Corners.cs` turn detection + tips,
  `LimitModel.cs` + `TargetPlan.cs` lap-time target, `Grip.cs` wheel events + tyres, `AcShm.cs` game memory,
  `Lap.cs` CSV laps (14 base columns + 27 wheel columns), `Settings.cs` (%APPDATA%\AC Coach\ustawienia.ini), `Lang.cs`

## Rules
- Every user-facing text: `L.T("polski", "english")`; turns are Z1.. (pl) / T1.. (en). Map script texts: `T('pl','en')`,
  static map HTML: `TrackMap.TemplateEn`. Check: `test.sh` fails on Polish letters in the English report.
- Keep data file names/formats (laps/*.csv, cel.txt, margines.txt, zakrety.txt, pikniecia.csv, rywale/, ini keys).
- Polish report must stay identical unless the change is intended (compare an old `raport_*.txt`).
- Bump version in `AssemblyInfo.cs` and `instalator/ACCoach.nsi`; update `JAK UZYWAC.txt` and `HOW TO USE.txt`.
- Branch: `claude/windows-multi-user-app-1h74bu`.
