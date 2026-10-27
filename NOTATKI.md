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
- **1.7** – poprawki po jeździe Mirka (Silverstone GP, McLaren MP4-12C GT3):
  - meta: licznik okrążeń i zerowanie czasu mogą przyjść w różnych klatkach (offline: praktyka, kariera) – wcześniej
    każde okrążenie było brane za restart i nic się nie zapisywało (odtworzone w AcSim `--order time|laps`);
  - głos: nowsza uwaga o zakręcie zastępuje starszą, przepada po dojechaniu do następnego zakrętu, krótsza wersja
    gdy mało czasu; po wyjeździe/obrocie/skoku pozycji tylko „Zakręt N, wyjazd”, bez liczb; różnica punktu
    hamowania > 120 m = to nie to samo hamowanie (było np. -140 m w Z7);
  - plan celu: zysk w zakręcie bywa na dojeździe (niewykryty szybki zakręt, np. Maggotts) – raport pokazuje
    miejsce i podział dojazd/wejście/wyjście zamiast „szczyt 131 → 131 km/h”;
  - mapa: u Mirka gra nie podaje poślizgu kół ani TC/ABS (same zera – AC < 1.14?) – wykresy poślizgu ukryte, raport
    o tym pisze; temperatury bez skoku do 0 na starcie;
  - log okna zapisywany do `<dane>\log.txt` (+ `log_prev.txt`).
- **1.7.1** – Defender blokował instalator 1.7.0 jako `Trojan:Win32/Ulthar.A!ml` (heurystyka ML, fałszywy alarm).
  Próba: instalator kompresowany zlib zamiast solid lzma, pełne dane wersji (firma, copyright) w instalatorze i exe.
  Niezweryfikowane – prawdziwe rozwiązanie: zgłoszenie do Microsoftu (wdsi/filesubmission) albo podpis cyfrowy.
- **1.8** – aplikacja w grze (Python, `apps\python\ACCoach`): licznik metrów do punktu hamowania (ujemne po
  minięciu, werdykt OK/WCZEŚNIE/PÓŹNO), paski hamulca i gazu z idealnego przejazdu (kolor = faza), delta do
  najlepszego okrążenia. AC Coach wgrywa ją sam, włącza w python.ini i zapisuje `data\<combo>.txt`.
- **1.8.1** – mapa w analizie odtwarza aplikację w grze na wybranym okrążeniu (ta sama logika w JS co `acapp.py`,
  dane z `<combo>\aplikacja_w_grze.txt`): okienko jak w grze, odtwarzanie, wykres licznika, paski na wykresach,
  na mapie punkty 0 m i oceny. Zmiana logiki w `acapp.py` = ta sama zmiana w `appSim` w `map_template.html`.
- **1.8.2** – pętla „Okrążenie wyjazdowe / niepełne – pomijam” (u jerzu, Vallelunga): metę z samego zerowania czasu
  (ścieżka z 1.7) brał też przy aucie stojącym tuż przed linią (boks) – każde zerowanie = „okrążenie”, nic się nie
  zapisywało. Teraz tylko gdy auto jedzie (> 15 km/h), przesunęło się kilka metrów do przodu i czas szedł > 10 s;
  komunikat najwyżej co 30 s. Odtworzone w AcSim `--pits N` (stoi przed linią, czas startuje od nowa) – zachowanie
  gry w boksie to hipoteza. `log.txt` ma teraz wiersze `[line]` (licznik, czas, pozycja, prędkość przy każdej zmianie).
- **1.9** – aplikacja w grze przebudowana po jeździe jerzu („paski B/G w niczym nie pomagają”, „hamowałem później
  i było szybciej, a licznik pisał POZNO”): paski usunięte. Zasada: nic w trakcie zakrętu poza licznikiem (znika po
  wciśnięciu hamulca); przed zakrętem jedna wskazówka (`Tip.Cue`, krótkie polecenie z `Corners.MakeTip`), zaraz po
  zakręcie wynik 3 s (czas vs wzorzec, REKORD, różnice vs najlepszy przejazd), zakręt cichnie po 3 dobrych
  przejazdach. Trener pisze `<combo>.live.txt` w grze po każdym zakręcie, `aplikacja_wskazowki.txt` (wskazówki
  i serie między sesjami) i `aplikacja_log.txt` (co aplikacja pokazała na każdym okrążeniu – mapa to odtwarza).
  Dane gry `accoach 2`: linie `turn`, siatka tylko czas + prędkość.

## Do sprawdzenia na prawdziwej grze (nic z tego nie było testowane na Windows z AC)
- Offsety pamięci kół (AcShm.cs: 640 slipRatio, 656 slipAngle, 72 load, 152 core, 88 pressure, 348 brakeTemp,
  672 tc, 676 abs, 300 yaw) – wzięte z dokumentacji AC, niezweryfikowane.
- Progi zdarzeń kół (Grip.cs: blokada < -0,25 przez 0,08 s, buksowanie > 0,2, uślizg tył > 1,4 × przód…) – do
  dopasowania na prawdziwych okrążeniach.
- Głos angielski/polski, głośności, okno na prawdziwym Windows (DPI, wygląd), zamknięcie okna krzyżykiem.
- Kolejność licznik/czas na mecie offline – poprawka na hipotezie (obie kolejności obsłużone); potwierdzić w log.txt.
- Pętla „Okrążenie wyjazdowe” (1.8.2): potwierdzić w `log.txt` (wiersze `[line]`), co gra podaje w boksie.
- Wykrywanie zakrętów: na Silverstone GP 10 zakrętów zamiast 18 (szybkie łuki bez hamowania nie są wykrywane).
- Aplikacja w grze (1.8/1.9): czy AC ją ładuje (Python 3.3, py_log.txt), sekcja `[ACCOACH] ACTIVE=1` w python.ini,
  wyrównanie etykiet, dwie linie tekstu („\n” w etykiecie), setBackgroundOpacity, zapis do folderu gry
  (uprawnienia Steam), widoczność w VR. Testowana tylko atrapą `ac` (tools/test/check_acapp.py).
- Progi wskazówek z Corners.cs strojone na 26 okrążeniach Huracán ST / Vallelunga – sprawdzić na innych autach.

## Odłożone tematy
- **Sprzedaż**: model „płatne pobranie” (Lemon Squeezy/Paddle/Gumroad/itch.io), zapytanie do Kunos, wersja angielska
  (zrobiona), film „przed i po”, obsługa ACC/AC EVO. Szacunek: dziesiątki–setki płatnych pobrań w 1. roku.
- **Podpis cyfrowy**: Certum w chmurze (od ok. 49 €) albo SignPath (tylko open source). Po zakupie: podpisywanie
  exe/instalatora (`signtool`, w NSIS `!finalize` / `!uninstfinalize`).
- **AI**: podsumowanie po jeździe przez Claude API (wymaga internetu i klucza).
- **Próbkowanie 333 Hz** (dziś ~90 Hz wg klatek gry): bez wpływu na wydajność, pliki 3–4× większe.
- Model granic z rzeczywistego poślizgu opon (gdy będą prawdziwe dane kół).

## Wydania
Pliki mają wersję w nazwie (`AC_Coach_Setup_1.7.1.exe`, `AC_Coach_portable_1.7.1.zip`): pobrany obok starszego jako
„AC_Coach_Setup (1).exe” był blokowany przez Defendera (`Trojan:Win32/Ulthar.A!ml`). Wersja brana z `instalator/ACCoach.nsi`.
- W repozytorium (katalog główny) leży tylko bieżąca wersja – przy nowej: `git rm` starych, skopiować z `dist/`.
- Workflow `.github/workflows/release.yml` (tag `v*`) buduje na Ubuntu i publikuje oba pliki jako GitHub Release.
  Stały link dla użytkowników: `https://github.com/jerzu13/nowe/releases/latest` (strona najnowszego wydania).
- Darmowy podpis: tylko SignPath Foundation (wymaga licencji open source – koliduje z planem sprzedaży).
