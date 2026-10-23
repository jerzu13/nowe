# AC Coach – notatki projektu

## Historia wersji
- **1.0** (zip od użytkownika) – trener w konsoli + pliki .bat, dane w Dokumentach.
- **1.1** – działa na każdym Windows/użytkowniku: dane obok exe, szukanie AC (Steam, dyski, sciezka_ac.txt),
  instalator NSIS dla bieżącego użytkownika, okno konfiguracji, ikona.
- **1.2** – okno „na żywo” w trakcie jazdy (piknięcia, głos, fokus, głośności).
- **1.3** – cel czasu okrążenia: model granic auta z własnych okrążeń, ocena realności, cele pośrednie, plan na zakręty;
  głos mówi o najlepszym okrążeniu i o celu. 1.3.1 – mapka „Gdzie to jest na torze” przy wykresach.
- **1.4** – dane kół i opon (poślizg, kąt poślizgu, obciążenie, temperatury, ciśnienia, TC/ABS), wykrywanie blokad,
  buksowania, nad-/podsterowności; sekcja KOŁA I OPONY, nowe wykresy.
- **1.5** – jedno okno aplikacji (zakładki), log zamiast konsoli, instalator z jednym skrótem.
- **1.6** – wersja angielska domyślna (polski w Ustawieniach / w instalatorze), HOW TO USE.txt.
- Porządki (po 1.6): podział LiveCoach/MainForm na pliki partial, build.sh/test.sh, hook startowy, workflow Release.

## Do sprawdzenia na prawdziwej grze (nic z tego nie było testowane na Windows z AC)
- Offsety pamięci kół (AcShm.cs: 640 slipRatio, 656 slipAngle, 72 load, 152 core, 88 pressure, 348 brakeTemp,
  672 tc, 676 abs, 300 yaw) – wzięte z dokumentacji AC, niezweryfikowane.
- Progi zdarzeń kół (Grip.cs: blokada < -0,25 przez 0,08 s, buksowanie > 0,2, uślizg tył > 1,4 × przód…) – do
  dopasowania na prawdziwych okrążeniach.
- Głos angielski/polski, głośności, okno na prawdziwym Windows (DPI, wygląd), zamknięcie okna krzyżykiem.
- Progi wskazówek z Corners.cs strojone na 26 okrążeniach Huracán ST / Vallelunga – sprawdzić na innych autach.

## Odłożone tematy
- **Sprzedaż**: model „płatne pobranie” (Lemon Squeezy/Paddle/Gumroad/itch.io), zapytanie do Kunos, wersja angielska
  (zrobiona), film „przed i po”, obsługa ACC/AC EVO. Szacunek: dziesiątki–setki płatnych pobrań w 1. roku.
- **Podpis cyfrowy**: Certum w chmurze (od ok. 49 €) albo SignPath (tylko open source). Po zakupie: podpisywanie
  exe/instalatora (`signtool`, w NSIS `!finalize` / `!uninstfinalize`).
- **AI**: podsumowanie po jeździe przez Claude API (wymaga internetu i klucza).
- **Próbkowanie 333 Hz** (dziś ~90 Hz wg klatek gry): bez wpływu na wydajność, pliki 3–4× większe.
- Model granic z rzeczywistego poślizgu opon (gdy będą prawdziwe dane kół).

## Wydania (punkt 4 z porządków – przygotowane, nie dokończone)
Workflow `.github/workflows/release.yml` buduje na Ubuntu i publikuje `AC_Coach_Setup.exe` + `AC_Coach_portable.zip`.
1. Na GitHubie: Releases > Draft a new release > tag `v1.6.0` (nowy), gałąź `claude/windows-multi-user-app-1h74bu`
   (albo `master` po scaleniu) > Publish. Workflow dołączy pliki (nie było jeszcze testowane – sprawdzić Actions).
2. Stały link: `https://github.com/jerzu13/nowe/releases/latest/download/AC_Coach_Setup.exe`
3. Dopiero potem usunąć binaria z repozytorium (link „raw” do gałęzi przestanie działać):
   `git rm --cached AC_Coach_Setup.exe AC_Coach_portable.zip ACCoach/ACCoach.exe` i dopisać je do `.gitignore`.
   Historia gita nadal je zawiera; jej czyszczenie wymaga przepisania historii – tylko na wyraźną prośbę.
