# Plan dalszej implementacji TrafficSign-Si

Data: 2026-11-09. Gałąź: `debloat`. Punkt odniesienia: `618158c`.
Status: etapy wykonawcze 1–5 wdrożone; lokalny i niezależny odbiór końcowy
zakończone. Etap 9 pozostaje do realizacji na rzeczywistych danych.

## 1. Cel i stan wyjściowy

Projekt ma działający techniczny przepływ: katalogi klas → preprocessing
`gray32-v1` → MLP → model JSON → klasyfikacja w WinUI. Core jest wspólny
dla trenera i GUI. Dotychczasowe 26 przypadków testowych przechodzi.
Skuteczność na prawdziwych znakach i pełna obsługa GUI nie zostały odebrane.

Celem tej iteracji jest czytelniejszy trener, wiarygodne testy kontraktów
i utrzymanie zgodności istniejących modeli oraz zapisanych eksperymentów.
Usunięto wcześniejszy plan i raport: historię zachowuje Git. Ten dokument
jest aktualnym planem i po wdrożeniu ma zawierać rzeczywisty wynik odbioru.

## 2. Zasady projektowe i granice zakresu

- Czytelność jest kryterium odbioru, nie skutkiem ubocznym formatowania.
  Metody na wysokim poziomie mają pokazywać przebieg operacji, szczegóły
  walidacji i plików należy przenieść do nazwanych kroków.
- Wydzielać prywatne metody albo małe konkretne klasy według odpowiedzialności.
  Bez DI, hierarchii interfejsów, ogólnego frameworka pipeline i przebudowy GUI.
- Jasne nazwy, jedna instrukcja na linię, klamry przy zagnieżdżonych pętlach.
  Krótkie indeksy są dozwolone w matematyce. Komentarze wyjaśniają decyzje.
- Nie zmieniać algorytmu podziału, kolejności losowań, seeda, wyboru najlepszej
  epoki, wzorów matematycznych ani identyfikatora preprocessingu.
- Model v1 i istniejące poprawne manifesty/raporty zachowują zgodność.
  Jedyna zamierzona zmiana walidacji: manifest bez jawnej wersji jest błędem.
- Zachować CLI, nazwy wyjść, camelCase JSON, brak nadpisywania oraz publikację
  modelu jako ostatniego znacznika kompletnego eksperymentu.
  Komunikaty parsera można doprecyzować o nazwę opcji i błędną wartość,
  zachowując dotychczasowe kody wyjścia i składnię poleceń.
- Nie dodawać RGB, augmentacji, CNN, kamery, detekcji, progów softmax ani
  optymalizacji pamięci bez pomiaru. Brak danych rzeczywistych nie blokuje tej iteracji.

## 3. Etap 1 — testy, które wykrywają regresję

1. Naprawić testy błędnych opcji CLI: poprawny tymczasowy zbiór, nieistniejące
   wyjścia, oczekiwany komunikat dla konkretnej opcji i brak wyników.
   Sprawdzić nieznaną opcję, zakres epok/hidden-size, NaN i zapis z przecinkiem,
   brak wartości i powtórzenie opcji. Test ma zawieść po usunięciu walidacji.
   Ustawić poprawne opcje pomocnicze na mały przebieg (np. jedna epoka), aby
   zepsuta walidacja nie uruchamiała domyślnego długiego treningu. Obecny
   int.Parse/double.Parse daje ogólne błędy formatu: dodać kontekst opcji
   w małym pomocniku parsera albo sprawdzać jednoznaczny błąd wartości,
   bez dodawania biblioteki CLI. Objąć także górne granice zakresów.
2. Dodać testy manifestu: brak formatVersion, nieobsługiwana wersja,
   null/nieprawidłowe entries i błędne przypisania; objąć tylko istotne
   niepokryte granice, bez testowania samych getterów.
3. Utrwalić zgodność istniejącego formatu podziału i fingerprintu przed zmianą
   reprezentacji wewnętrznej. Test powinien opierać się na znanym kontrakcie
   lub niezależnym oczekiwanym wyniku, nie kopii implementacji.
   Przed refaktoryzacją zapisać oczekiwane przypisania dla ustalonego seeda
   i fingerprint małej deterministycznej próbki. Sam round-trip nowego kodu
   nie wykryje jednoczesnej zmiany zapisu i odczytu. Nie uzależniać testów CI
   od lokalnego ignorowanego artifacts/demo.
4. Zachować testy wszystkich gradientów, round-trip modelu, EXIF, alpha,
   duplikatów/grup i pełnego treningu/oceny.

Odbiór: testy rozróżniają błąd argumentów od błędu danych; stare testy przechodzą,
nowe ujawniają brak wymaganej wersji przed poprawką.

## 4. Etap 2 — jawny kontrakt manifestu i czytelne dane

1. SplitManifest musi wymagać jawnego formatVersion; ToManifest i wszystkie
   miejsca tworzenia manifestu ustawiają 1. Odrzucać brak, zły typ i inną wersję.
2. W logice wewnętrznej użyć enum DatasetSplit (Train, Validation, Test).
   W JSON zachować dokładnie `train`, `validation`, `test` oraz opcjonalne null
   przed losowaniem. Użyć jawnego mapowania na granicy, aby nie zmienić JSON
   ani fingerprintu przez automatyczną serializację enuma.
   Obecny fingerprint hashuje kompaktowy JSON z nazwami PascalCase i kolejnością
   RelativePath, LabelIndex, Sha256, GroupId, Split; null GroupId też jest
   uwzględniane. Zachować tę reprezentację dokładnie. JSON manifestu/raportów
   używa innych opcji (camelCase i wcięcia); nie scalać tych serializerów.
3. W Dataset.Load zastąpić równoległe tablice opisujące plik jednym wewnętrznym
   typem przygotowywanej próbki: ścieżka, etykieta, hash, grupa i przypisanie.
   Tablica rodziców union-find może pozostać, bo opisuje inny algorytm.
4. Wydzielić odkrywanie obrazów, odczyt/walidację manifestu, hashe/metadane,
   łączenie jednostek niezależnych, przypisanie podziałów, kontrolę pokrycia,
   dekodowanie i obliczenie fingerprintu. Dopasować podział metod do realnego
   kodu; nazwy kroków są propozycją, nie obowiązkową listą klas.
5. Zachować sortowanie ordinal, grupowanie przechodnie, konflikty etykiet,
   oficjalny test i walidację wydzielaną tylko z train oraz limity danych.

Odbiór: Load da się przeczytać jako sekwencję nazwanych operacji; zapisany
manifest daje identyczne przypisania i fingerprint niezależnie od seeda odczytu;
dotychczasowy poprawny zapis eksperymentu nadal daje się ocenić.

## 5. Etap 3 — typowane raporty i orkiestracja treningu

1. Zastąpić List<object> przez List<EpochMetrics>; wprowadzić konkretne DTO
   raportu treningowego/testowego i potrzebnych elementów (baseline, liczności).
   Zachować pola i strukturę JSON. Unikać jednego pliku na każdą drobną parę pól.
2. Training.Run ma pokazywać walidację wyjść, ładowanie danych, uczenie,
   wybór najlepszego modelu, budowę raportu i publikację. Wydzielić kroki,
   których szczegóły przesłaniają ten przebieg.
3. Wydzielić wspólne wyznaczanie ścieżek i zapis/publikację wyników do małego
   konkretnego pomocnika. Zachować sprawdzanie kolizji przed treningiem,
   pliki tymczasowe obok celów i brak nadpisywania. Sprzątanie usuwa tylko
   pliki utworzone przez bieżące wywołanie; błąd sprzątania nie powinien
   ukrywać pierwotnego błędu ani przerywać prób sprzątania pozostałych plików.
4. EvaluateSaved ma czytelnie oddzielać weryfikację tożsamości eksperymentu,
   metryki i zapis. W razie typowanego odczytu nie wymuszać nowych pól na
   starym raporcie poza obecnym wymaganym nagłówkiem v1/hash/fingerprint.
5. Zachować remis na korzyść wcześniejszej epoki, baseline z train, brak
   testowych metryk podczas treningu i ochronę przed zmianą modelu/danych.

Odbiór: brak anonimowego trwałego kontraktu i List<object>; test end-to-end
sprawdza raporty, ponowną ocenę oraz odmowę nadpisania. Dodać test błędu
publikacji po pomyślnej kontroli wstępnej: sprawdzić cofnięcie już opublikowanego
podziału, brak modelu i zachowanie zastanego pliku powodującego kolizję.
Można utworzyć kolizję podczas komunikatu epoki przez testowy TextWriter,
wykorzystując istniejący parametr output, bez interfejsu systemu plików.
Obsługa niepowodzenia sprzątania ma zachować pierwotny wyjątek, próbować
pozostałych plików i dołączyć informację o pozostawionych artefaktach.

## 6. Etap 4 — jedno przejście sieci przy ocenie

1. Dodać mały wynik operacji oceny próbki zawierający prawdopodobieństwa
   i stratę albo przewidziany indeks i stratę. Wybrać najprostszy kontrakt
   przydatny trenerowi. Predict i Loss pozostają dostępne.
2. Współdzielić stabilne obliczenie cross-entropy z istniejącym Loss;
   Evaluate nie wywołuje już oddzielnych forward dla predykcji i straty.
3. Uporządkować formatowanie kodu matematycznego bez przebudowy backprop.
   Ujednolicić prywatne pola w dotykanych plikach; nie formatować całego repo.

Odbiór: wynik nowej operacji zgadza się z Predict/Loss, w tym dla logitów
rzędu 1e16; gradienty i zapamiętanie małej próbki nadal przechodzą.
Nie deklarować przyspieszenia bez benchmarku.

## 7. Etap 5 — automatyczna weryfikacja i dokumentacja

1. Dodać prosty workflow GitHub Actions na Windows: wybór SDK zgodnie
   z global.json, locked restore, build Release x64 i testy. Bez publikacji,
   sekretów i instalacji aplikacji. Jeśli hostowany runner nie obsługuje
   wymagań WinUI, opisać ograniczenie; nie ogłaszać zdalnego sukcesu bez runu.
   Wskazać windows-2025 i użyć setup-dotnet z global-json-file. Workflow
   uruchamiać na push/pull_request/workflow_dispatch, uprawnienia contents: read.
   Testy w CI uruchamiać z --no-build --no-restore po buildzie tej samej
   konfiguracji. Nie wyłączać błędów builda przez continue-on-error.
2. README: poprawić link do nowego planu i opisać tylko zmienione kontrakty
   oraz komendy. Zachować uczciwe ograniczenia technicznego demo.
3. Ten plan po wdrożeniu: status etapów, konkretne polecenia i wyniki,
   uwagi recenzenta, ograniczenia oraz zadania pozostałe do odbioru.
   Nie tworzyć ponownie rozbudowanej historii sprzątania w osobnym raporcie.

## 8. Weryfikacja końcowa

- `dotnet restore TrafficSign-Si.slnx -p:Platform=x64 --locked-mode`
- `dotnet build TrafficSign-Si.slnx -c Release -p:Platform=x64 -p:UseSharedCompilation=false --no-restore`
- `dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj -c Release --no-build --no-restore`
- Sprawdzenie istniejącego lokalnego demo (jeśli dostępne): ocena starego
  modelu/podziału/raportu bez nadpisania oryginalnego wyniku: skopiować
  wyłącznie signs.json, signs.report.json i signs.split.json do nowego
  katalogu pod artifacts i wykonać evaluate ze starym artifacts/demo-data.
  CLI sam wyznacza ścieżkę test-report, więc nie przekazywać nieistniejącej
  opcji wyjścia. To dodatkowy odbiór lokalny obok przenośnego testu kontraktu.
- `git diff --check` i ręczny przegląd diffu: nazwy, odpowiedzialności,
  niepotrzebne abstrakcje, przypadkowe zmiany JSON i kolejności losowań.

Nie dodawać zależności produkcyjnych, nie commitować danych ani modeli,
nie wykonywać push ani publikacji. Zgłosić ograniczenia środowiska oddzielnie
od błędów implementacji. Nie ogłaszać pełnego odbioru GUI na podstawie buildu.

## 9. Następny etap wymagający rzeczywistych danych

Pozostaje poza wykonawczym zakresem tej iteracji, bo brakuje danych i wymagań:

1. Potwierdzić wymagania zaliczenia i docelowy komputer demonstracyjny.
2. Ustalić źródło, wersję, licencję, klasy i metadane serii rzeczywistego
   zbioru; dodać ich identyfikację do raportu eksperymentu przed pomiarem.
3. Przygotować niezależne podziały, zmierzyć baseline, czas i pamięć treningu,
   dobrać ustawienia tylko na walidacji; test uruchomić po zakończeniu strojenia.
4. Sprawdzić pełny GUI: anulowanie, błędny JSON/wymiary/obraz, zmiana modelu,
   wyczyszczenie starego wyniku, zgodność z Core i obsługa Narratora.
5. Uruchomić aplikację na komputerze demonstracyjnym. RGB lub optymalizacje
   rozważyć dopiero na podstawie pomyłek i zmierzonego kosztu.

## 10. Recenzja i wynik wdrożenia

### Recenzja planu — 2026-11-09

Plan zaakceptowano po przeglądzie kodu trenera, sieci, testów i konfiguracji.
Zakres odpowiada problemom czytelności i pozostaje wykonalny bez rzeczywistego
zbioru ani przebudowy GUI. Najważniejsze doprecyzowania recenzenta:

- Fingerprint ma własny historyczny format PascalCase, odrębny od manifestu
  camelCase; enum musi być mapowany jawnie także w materiale do hashowania.
- Test zgodności musi mieć oczekiwany wynik sprzed refaktoryzacji. Sam zapis
  i odczyt nowego kodu nie jest dowodem kompatybilności.
- Parser może dostać kontekstowe komunikaty błędu. Testy błędnych opcji mają
  korzystać z krótkiego poprawnego przebiegu i obejmować oba końce zakresów.
- Cofnięcie publikacji wymaga testu rzeczywistej kolizji po preflight;
  obecny parametr TextWriter pozwala taki scenariusz wywołać bez nowego frameworka.
- Zdalny CI pozostaje niepotwierdzony do uruchomienia workflow. Lokalny
  odbiór starego demo wymaga skopiowania kompletu trzech plików do nowego katalogu.

Recenzent zmienił wyłącznie ten plan; nie wdrażał kodu ani nie powtarzał testów.
Wyniki wdrożenia i końcowego odbioru są zapisane poniżej.

### Wdrożenie po recenzji — 2026-11-09

| Etap | Status | Rezultat |
| --- | --- | --- |
| 1. Testy kontraktów | Wdrożony | Poprawne dane w testach błędnych opcji, konkretne komunikaty, kontrola braku wyjść; snapshot sześciu stałych PNG z wynikiem sprzed refaktoryzacji. |
| 2. Przygotowanie danych | Wdrożony | Wymagana wersja manifestu, DatasetSplit w logice, jawne mapowanie nazw JSON, PreparedImage zamiast równoległych tablic metadanych, nazwane kroki DatasetLoader. |
| 3. Trening i raporty | Wdrożony | EpochMetrics i typowane raporty, oddzielona pętla uczenia, wspólne ścieżki i publikacja ExperimentFiles, odczyt tylko wymaganego nagłówka starego raportu. |
| 4. Ocena próbki | Wdrożony | EvaluateSample zwraca indeks i stratę z jednego forward; Predict i Loss pozostają dostępne. |
| 5. CI i dokumentacja | Wdrożony lokalnie | Workflow Windows z locked restore, buildem Release i testami; README wskazuje aktualny plan. Zdalny run GitHub nie był wykonywany. |

Kod ma teraz konkretne granice odpowiedzialności: Dataset zawiera publiczne
dane i manifest, DatasetLoader przygotowuje obrazy i podziały, Training
koordynuje eksperyment, ExperimentReports definiuje kontrakty, a ExperimentFiles
weryfikuje tożsamość i publikuje wyniki. Nie dodano zależności produkcyjnych
ani frameworka infrastruktury. Przejście sieci ma nazwany ForwardState.

Wynik snapshotu utrwalonego przed zmianą implementacji:
`92EB9AD6466B197416DE74C4B13DECCDA8C4030ACF87EBCED53CB752535A4A73`.
Test porównuje również konkretne przypisania klas a/b dla seeda 42 i ponowny
odczyt zapisanego manifestu z innym seedem. Stałe bajty PNG uniezależniają
ten kontrakt od kodera obrazów użytego do generowania fixture.

Test kolizji po kontroli wstępnej tworzy obcy raport podczas komunikatu epoki.
Podział zdąża się opublikować, zapis raportu zawodzi, a wycofanie usuwa podział
i pliki tymczasowe, zachowując zastany raport i nie publikując modelu.
Sprzątanie próbuje pozostałych plików mimo pojedynczego błędu i zachowuje
pierwotny wyjątek; diagnostyka pozostawionych plików trafia do output oraz
Exception.Data. W ModelFile.Save analogiczna diagnostyka wewnętrznego pliku
tymczasowego trafia do Exception.Data, a CLI wypisuje ją na stderr.

Weryfikacja wykonawcy:

```powershell
dotnet restore TrafficSign-Si.slnx -p:Platform=x64 --locked-mode
dotnet build TrafficSign-Si.slnx -c Release -p:Platform=x64 -p:UseSharedCompilation=false --no-restore
dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Locked restore zakończył się sukcesem. Build całej solucji: 0 ostrzeżeń,
0 błędów. Testy: **40 zaliczonych, 0 błędów, 0 pominiętych**. `git diff --check`
nie znalazł błędów whitespace; Git informował o oczekiwanej normalizacji
LF do CRLF w plikach C# zgodnie z istniejącą konfiguracją.

Pierwsze uruchomienie testów w sandboxie nie zwracało diagnostyki i pozostawiło
pustą sesję narzędzia. Późniejsze sprawdzenie CIM poza sandboxem nie znalazło
procesu dotnet ani PowerShell z tą komendą. Końcowy restore/build/test wykonano
poza sandboxem. Wcześniejsze ostrzeżenia NU1900 z restore lokalnej próbki
nie wystąpiły w końcowym locked restore ani buildzie Release.

Workflow korzysta z oficjalnego interfejsu
[actions/setup-dotnet](https://github.com/actions/setup-dotnet) i global.json.
Nie przeprowadzono jeszcze zdalnego runu, pełnego odbioru GUI ani pomiaru na
znakach. Nie wykonano commitu, push ani publikacji aplikacji.

### Niezależny odbiór końcowy

Agent nadrzędny sprawdził stary eksperyment z commitu bazowego, kopiując trzy
oryginalne pliki modelu/raportu/podziału do nowego katalogu:
`artifacts/compatibility-a48704adb57b442eb5d2c92afd5cc363`.

- `evaluate` starego modelu i podziału na oryginalnych danych: **8 próbek,
  loss 0.1109069157283888, accuracy 1.0**.
- Ponowne trenowanie 48 starych obrazów z seed 42, 10 epokami, hidden-size 8
  i learning-rate 0.01 wygenerowało dokładnie ten sam SHA256 modelu:
  `C6B40D3542F6D6F05255872C0823C7E54F633D3A515FAAECE2B647E638BE9E1B`.
- Fingerprint, wybrana epoka, strata wybranej walidacji, cała historia metryk
  oraz nazwy i kolejność pól raportu pozostały identyczne.

To dowody zgodności w tym samym środowisku i na syntetycznych wzorach.
Nie deklarują bitowej zgodności na innych komputerach ani skuteczności
rozpoznawania prawdziwych znaków.
