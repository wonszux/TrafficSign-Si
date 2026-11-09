# Plan uproszczenia TrafficSign-Si

Przegląd i aktualizacja planu: 9 listopada 2026. Branch: `debloat`.
Status: zaimplementowano A i techniczne MVP B; pomiar na znakach i pełny odbiór
GUI pozostają do wykonania. Sekcje 1–2 opisują stan
wyjściowy; aktualny zakres wdrożenia i dowody weryfikacji są w sekcji 10.
Checkboxy pozostają kryteriami odbioru; sam istniejący kod nie dowodzi odbioru.

## 0. Ocena i sposób korzystania z planu

Kierunek jest dobry: jeden ekran, wspólna biblioteka obliczeniowa, mało
zależności i brak architektury budowanej na zapas. Plan wymaga jednak
rozdzielenia dwóch różnych rezultatów:

- **A — debloat:** mniejszy, czytelniejszy i odtwarzalnie budowany szkielet,
  z dokumentacją zgodną z rzeczywistym zachowaniem.
- **B — MVP:** nowa funkcjonalność: dane → trening → model → predykcja w GUI.

Zakończenie A jest osobnym punktem kontrolnym. B zależy od wymagań zadania
i danych; nie należy traktować całego algorytmu uczenia jako warunku
ukończenia porządków. Osobne commity dla konfiguracji, zmian nazw i funkcji
ułatwią znalezienie przyczyny błędu i ewentualne cofnięcie konkretnej zmiany.

Więcej szczegółów jest potrzebne przede wszystkim w kontraktach danych,
kolejności zależności i kryteriach odbioru. Nie ma potrzeby projektowania
wszystkich metod ani tworzenia pustych klas na podstawie tego dokumentu.
Etapy wykonawcze i warunki ich zakończenia są w sekcji 6.

## 1. Stan wyjściowy przed implementacją

Podstawą planu jest przegląd kodu C#, XAML, konfiguracji projektów,
manifestów, ustawień uruchamiania, reguł Git i dokumentacji.
Projekt zawiera obecnie szkielet, a nie działające rozpoznawanie znaków:

- `TrafficSign-Trainer/src/Program.cs` przyjmuje folder i istniejący JSON,
  zbiera ścieżki plików według rozszerzeń i odczytuje tablicę wag.
  Nie dekoduje zdjęć, nie odczytuje etykiet i nie trenuje modelu.
- `NeuralNetwork.cs` przechowuje jedynie `double[]`. Nie ma warstw, biasów,
  funkcji aktywacji, predykcji ani uczenia.
- `TrafficSign-Detector` uruchamia okno z pustą stroną. Nie ma wyboru zdjęcia,
  wczytywania modelu ani prezentacji wyniku.
- Projekty nie współdzielą kodu i nie mają połączenia trener → model → GUI.
- Nie ma zbioru danych, przykładowego modelu ani testów.

Najwięcej nadmiaru pochodzi z szablonów. Nie warto teraz projektować dużej
architektury; najpierw potrzebny jest mały, kompletny przepływ działania.

## 2. Problemy stwierdzone w kodzie wyjściowym

| Miejsce | Ustalenie | Proponowana zmiana |
| --- | --- | --- |
| `Program.cs` / README | Pusty folder daje sukces, choć README deklaruje błąd. | Walidować brak zdjęć i zakończyć kodem 1. |
| `Program.cs` / README | `[]` jest akceptowane, choć README wymaga niepustych wag. Brakuje jawnej kontroli skończoności wartości po odczycie. | Sprawdzać długość, `double.IsFinite`, a docelowo zgodność wymiarów modelu. |
| `NeuralNetwork.cs` | Publiczne `readonly double[] weights` pozwala zmieniać elementy tablicy; konstruktor zachowuje tablicę wywołującego. | Schować wagi w klasie; jawnie ustalić własność danych, np. skopiować wejście. Nie tworzyć rozbudowanej warstwy kolekcji tylko dla tej klasy. |
| `Program.cs` | Rozszerzenie pliku jest traktowane jako wystarczające do policzenia zdjęcia. | Podczas przygotowania danych naprawdę dekodować obraz i raportować uszkodzone pliki. |
| `Program.cs` | Brak etykiet, przeszukiwanie wyłącznie jednego katalogu. | Przyjąć jeden prosty format zbioru: podfoldery klas i obrazy w nich. |
| `Program.cs` | Narzędzie nazwane trenerem wyłącznie odczytuje istniejące wagi. | Docelowo folder danych jako wejście, plik nowego modelu jako wyjście; odczyt modelu dopiero przy predykcji. |
| `TrafficSign-Detector.csproj` | Trzy zależności mają `Version="*"`. | Przypiąć konkretne, wspólnie sprawdzone wersje z działającego restore. Nie aktualizować wszystkiego przy okazji sprzątania. |
| `TrafficSign-Detector.csproj` | Konfiguracje inne niż Debug ustawiają `PublishTrimmed` i `PublishReadyToRun`. | Na początek wyłączyć optymalizacje publikacji; osobno sprawdzić build Release i docelowy sposób dystrybucji. Sam build nie weryfikuje opublikowanej aplikacji. |
| Środowisko budowania | Brak `global.json`; lokalne `dotnet` wybiera zainstalowany SDK 11 preview, choć projekty celują w .NET 10. | Ustalić SDK 10 w `global.json`, z rozsądnym roll-forward dla poprawek. |
| `App.xaml.cs`, XAML | Wiele nieużywanych usingów i komentarzy instruktażowych z szablonu. | Usunąć; zostawić komentarze wyjaśniające decyzje i matematykę. |
| `Package.appxmanifest` | Zadeklarowano `systemAIModels`, choć kod z tego nie korzysta; są też pozostałości PhoneIdentity. | Usunąć nieużywane deklaracje po sprawdzeniu budowania i uruchamiania wybranego wariantu aplikacji. |
| `Properties/PublishProfiles`, `.gitignore` | Profile `win-x64.pubxml`, `win-x86.pubxml` i `win-arm64.pubxml` istnieją lokalnie, ale nie są śledzone; reguła `*.pubxml` je ignoruje. `.csproj` korzysta z profilu tylko wtedy, gdy plik istnieje. | Ustalić, czy profil jest potrzebny. Potrzebny, pozbawiony danych prywatnych profil objąć wyjątkiem w `.gitignore` i śledzić albo przenieść potrzebne ustawienia do projektu. Nie uzależniać działania od plików obecnych wyłącznie lokalnie. |
| `.gitignore`, `.gitattributes` | Duże, ogólne szablony obejmują technologie nieobecne w projekcie. | Skrócić do reguł .NET/Visual Studio/WinUI i danych projektu; zachować ignorowanie buildów, ustawień użytkownika i certyfikatów. |

Nie usuwać automatycznie ikon, manifestów ani pakietów WinUI: część jest potrzebna
do uruchamiania lub jest wskazana w konfiguracji. Przykładowo `LockScreenLogo`
nie jest wskazane w manifestach, ale jest wpisem `Content` w `.csproj`.
Usuwając taki zasób, poprawić również wpis projektu i sprawdzić pakiet.

## 3. Proponowany zakres na studia

Pierwsza wersja: klasyfikacja zdjęcia zawierającego jeden, już wykadrowany znak,
początkowo np. 5–10 klas. Rozmiar wejścia, liczbę klas i wyniki ustalić na danych.

Odnajdywanie wielu znaków na całym zdjęciu ulicy to osobne zadanie detekcji.
Nie dodawać go do pierwszej wersji, podobnie jak kamery, filmu i pracy na żywo.
W opisie projektu jasno nazwać przyjęty zakres.

Nie znamy wymagań prowadzącego dotyczących samodzielnego napisania sieci.
Roboczo przyjmujemy prostą własną sieć MLP. Sama obecność klasy
`NeuralNetwork` nie przesądza wymagań ani trafności tej metody.
Przed implementacją algorytmu potwierdzić wymagania zadania.
Jeżeli dozwolony jest gotowy model, może to zmienić zakres trenera.

Proponowany punkt startowy: obraz 32×32 w skali szarości, wartości [0, 1],
jedna warstwa ukryta i wyjście odpowiadające klasom. To punkt wyjścia do pomiaru,
nie obietnica skuteczności. Jeśli klasy różnią się kolorem, porównać wariant RGB.
Bez CNN, GPU i dodatkowych frameworków w pierwszej iteracji.

Przed częścią B zapisać tutaj ustalenia:

| Decyzja | Propozycja robocza | Co trzeba ustalić |
| --- | --- | --- |
| Wymagania zaliczenia | Własny MLP i klasyfikacja wykadrowanego znaku | Czy wymagany jest własny backprop, detekcja lub określona skuteczność? |
| Dane | 5–10 klas, JPEG/PNG, podfoldery klas | Nazwa i wersja zbioru, źródło, warunki użycia, etykiety, serie zdjęć i liczności klas. |
| GUI | Windows x64, jeden ekran | System i środowisko na komputerze demonstracyjnym. |
| Uruchamianie | Najpierw zachować wariant packaged obecnego szablonu | Potwierdzić uruchomienie; unpackaged rozważyć osobno, jeśli upraszcza oddanie projektu. |
| Pomiar | Accuracy, wyniki per klasa i macierz pomyłek | Próg wymagany przez prowadzącego; bez niego nie obiecywać arbitralnego procentu. |

Niewiadome dotyczące danych i algorytmu nie blokują przeglądu ani porządków A.
Wybór innego zakresu zadania wymaga aktualizacji B przed jego implementacją.

Na potrzeby zleconej implementacji przyjęto roboczy zakres z tabeli: własny
MLP, pojedynczy wykadrowany znak i Windows x64. Nie podano rzeczywistego
zbioru ani wymagań skuteczności, dlatego odbiór techniczny korzysta z własnych
syntetycznych wzorów; nie zastępuje pomiaru na danych znaków drogowych.

## 4. Co uprościć i usunąć

1. Przenieść `src/Program.cs` do katalogu projektu. Obecną klasę sieci
   przenieść lub zastąpić przy dodaniu `Core`, zamiast przenosić ją dwukrotnie.
2. Docelowe namespace: `TrafficSign.Trainer`, `TrafficSign.Detector`,
   `TrafficSign.Core`; pola prywatne, nazwy publiczne PascalCase.
   Zmieniając namespace GUI, poprawić `RootNamespace`, `x:Class` i używane
   `xmlns:local`. Nazwy katalogów i plików `.csproj` mogą zostać obecne;
   ich kosmetyczna zmiana nie jest warunkiem MVP.
3. Zostawić jeden ekran. Dla tego zakresu umieścić treść w `MainWindow`
   i usunąć pustą `MainPage` oraz `RootFrame` wraz z nawigacją.
4. Zostawić zwykłe event handlery w code-behind dla wyboru pliku i predykcji.
   Obliczenia trzymać poza oknem. Nie dodawać teraz MVVM, kontenera DI,
   repozytoriów, fabryk, event busa ani interfejsu dla każdej klasy.
5. Usunąć komentarze szablonowe i uporządkować odwołania do profili publikacji
   zgodnie z wybranym sposobem uruchamiania i dystrybucji.
   Ustalić jeden podstawowy sposób uruchamiania GUI i opisać go w README.
6. Ograniczyć konfiguracje do x64, jeżeli takie komputery będą używane na
   zajęciach; dostosować razem `.csproj` i `.slnx`. Pozostałe architektury
   zachować tylko wtedy, gdy są potrzebne do oddania projektu.
   Sprawdzić też zgodność `RuntimeIdentifier` z wybraną platformą;
   obecna wartość domyślna zależy od architektury procesu budującego.
7. Nie mnożyć obsługiwanych formatów obrazu. Na początek JPEG i PNG,
   ewentualnie PPM, jeśli wymaga go rzeczywisty zbiór i wybrany dekoder.
8. Nie dodawać bazy danych, serwera, logowania użytkowników, chmury,
   pluginów, rozbudowanego systemu konfiguracji ani własnego frameworka UI.

## 5. Co wprowadzić

### Wspólny kod bez dublowania

Dodać jedną małą bibliotekę `TrafficSign.Core`, używaną przez trener i GUI.
Rozdzielać odpowiedzialności dopiero wtedy, gdy istnieje dla nich kod:

- `ImagePreprocessor` — dekodowanie, zmiana rozmiaru, kanały i normalizacja.
- `NeuralNetwork` — obliczenia predykcji oraz parametry sieci.
- `ModelData` — serializowalne dane modelu i jego ustawienia.
- `ModelFile` — zapis/odczyt i kontrola poprawności, jeśli wydzielenie
  tych operacji realnie uprości kod.

W trenerze zostawić ładowanie oznaczonych danych, podział zbioru,
pętlę uczenia i ocenę. Jedna biblioteka do obrazów, dobrana do formatów
zbioru i warunków użycia; nie mieszać kilku dekoderów. Nie wybierać jej
wyłącznie na podstawie listy rozszerzeń w obecnym `Program.cs`.
W implementacji wybrano SkiaSharp 3.119.4 (MIT) i jeden wspólny dekoder.

Zależności projektów: `Trainer → Core` i `Detector → Core`; `Core` nie zna
okna, konsoli ani projektów wykonawczych. Celować z `Core` w `net10.0`, jeśli
wybrany dekoder na to pozwala. Dodać jeden projekt `TrafficSign.Tests`
z referencjami do `Core` i trenera: testować obliczenia oraz wydzieloną logikę
podziału danych bez uruchamiania `Main`. Bez osobnego frameworka testowania GUI.
Kod backprop i aktualizacji parametrów może być przy sieci w `Core`, żeby
test gradientu nie wymagał uruchamiania konsoli; orkiestracja zostaje w trenerze.

### Model zamiast luźnej tablicy

Zapisywać jeden plik JSON zawierający wersję formatu, rozmiar obrazu,
sposób przetwarzania i normalizacji, wymiary warstw, wagi, biasy oraz
uporządkowaną listę klas. Sprawdzać długości tablic i skończoność liczb.
Nie zapisywać właściwości, które można jednoznacznie wyliczyć z pozostałych.

Minimalny kontrakt v1 ustalić przed trenerem i GUI:

| Pole | Znaczenie i walidacja |
| --- | --- |
| `formatVersion` | Na początek tylko `1`; nieznana wersja daje czytelny błąd, bez próby zgadywania formatu. |
| `preprocessing` | Wymiary, tryb koloru i identyfikator ustalonej procedury, np. `gray32-v1`; nie dowolny język konfiguracji. |
| `hiddenSize` | Dodatnia liczba neuronów jednej warstwy ukrytej; ustalić praktyczny limit przed alokacją. |
| `labels` | Co najmniej dwie niepuste, unikalne etykiety w kolejności wyjść sieci. |
| `hiddenWeights`, `hiddenBiases` | Długości odpowiednio `H × D` i `H`. |
| `outputWeights`, `outputBiases` | Długości odpowiednio `K × H` i `K`. |

`D = szerokość × wysokość × kanały`, `H = hiddenSize`, `K = labels.Length`.
Wagi w płaskich tablicach: indeks `neuronWyjściowy × liczbaWejść + wejście`.
Aktywacja `tanh` i softmax są stałe dla formatu v1; nie dodawać wyboru wielu
architektur. Sprawdzać także brak pól wymaganych, `null`, skończoność liczb
i bezpieczne obliczenie rozmiarów. Odrzucać nieobsługiwane ustawienia wejścia.
Dotychczasowa luźna tablica wag nie jest modelem v1; nie ma danych potrzebnych
do jej automatycznej migracji i nie trzeba dodawać zgodności wstecznej.

Procedura `gray32-v1`: uwzględnienie orientacji EXIF, kompozycja przezroczystości
na białym tle, rozciągnięcie całego kadru do 32×32 z ustalonym filtrem
biliniowym, konwersja `Y = 0.299R + 0.587G + 0.114B`, następnie `Y / 255`.
Spłaszczenie wierszami od lewego górnego rogu. Kolejność i zaokrąglenia
utrwalić w testach dekodera. To świadomy prosty baseline; zmianę procedury
lub RGB oznaczyć nowym identyfikatorem i ponownie wytrenować model.

GUI musi używać dokładnie tego samego przetwarzania wejścia co trener.
Mapowanie indeks → etykieta musi pochodzić z modelu; nie może zależeć
od kolejności katalogów na komputerze użytkownika.
Podgląd w GUI nie jest wejściem sieci: predykcja używa oryginalnego pliku
i wspólnego preprocessora. Nie liczyć cech ze zrzutu kontrolki obrazu.

### Minimalny trening i uczciwy pomiar

- Jeden format danych: `data/<klasa>/<zdjęcie>`.
  Katalog główny zawiera katalogi klas, a one bezpośrednio obrazy;
  bez rekurencji i wyprowadzania etykiet z nazw plików. Obce pliki pomijać
  z podsumowaniem, uszkodzony obsługiwany obraz przerywa przygotowanie
  z podaniem ścieżki. Pusta klasa, mniej niż dwie klasy lub pusty zbiór
  są błędami; nie kontynuować na cicho zmienionych danych.
- Stałe ziarno losowania i ustalona kolejność klas/plików dla powtarzalności;
  sortowanie etykiet i ścieżek względnych przez `StringComparer.Ordinal`.
- Rozłączne zbiory treningowy, walidacyjny i testowy, z zachowaniem klas.
  Powiązane ujęcia tego samego znaku trzymać w jednej części, jeśli dane
  zawierają takie serie, żeby ograniczyć przeciek danych.
  Jeśli zbiór ma oficjalny podział, zachować go; przy oficjalnym train/test
  wydzielić walidację wyłącznie z train. W innym przypadku roboczo 70/15/15,
  z kontrolą obecności każdej klasy w każdej części. Za mała liczba
  niezależnych przykładów/grup daje błąd z licznościami; nie kopiować próbek
  między częściami, aby uzupełnić brakującą klasę. Identyfikatory grup wziąć
  z metadanych zbioru; gdy ich nie ma, opisać ograniczenie pomiaru.
- Zapisać podział: względna ścieżka, etykieta, część zbioru i identyfikator
  grupy, jeśli istnieje. Dodać skróty zawartości plików, aby wykryć identyczne
  kopie i zmianę danych. Duplikaty nie mogą trafić do różnych części;
  konflikt etykiet dla identycznego pliku jest błędem. Sam seed nie utrwala
  eksperymentu po dodaniu lub zmianie zdjęć.
- Opcjonalne `--split-manifest <json>` dostarcza oficjalne przypisania i grupy:
  `{ "formatVersion": 1, "entries": [{ "path": "klasa/001.png", "split": "train", "groupId": "seria-1" }] }`.
  Manifest obejmuje wszystkie obrazy. `split` ma wartości `train`, `validation`,
  `test` albo jest wszędzie `null`; `groupId` jest opcjonalne. Etykiety wynikają
  z katalogów, a zapisany manifest dodatkowo zawiera SHA256 każdego pliku.
  Grupa obejmuje jedną klasę. Przy braku metadanych serii raport jawnie
  ogranicza gwarancję do grupowania identycznych plików.
- Dla MLP: wagi i biasy, aktywacja warstwy ukrytej, stabilny softmax,
  cross-entropy i propagacja wsteczna; prosta pętla SGD.
  Roboczo `tanh`, inicjalizacja wag Xavier, biasy zerowe; SGD po jednej
  próbce, z przetasowaniem tylko treningu na każdą epokę. Softmax liczyć po
  odjęciu maksimum, a stratę z logitów przez log-sum-exp. Przerwać trening
  z komunikatem przy `NaN`/nieskończoności zamiast zapisywać wadliwy model.
- Kilka jawnych ustawień: liczba epok, learning rate, rozmiar warstwy ukrytej,
  seed. Zwykłe argumenty lub jedna mała klasa ustawień; bez rozbudowanego CLI.
- W konsoli strata i accuracy na epokę. Dobór ustawień na walidacji;
  końcowa accuracy i macierz pomyłek na odłożonym zbiorze testowym.
  Zachować parametry z epoki o najmniejszej stracie walidacyjnej;
  przy remisie wcześniejszą epokę. Test uruchomić dla wybranego modelu
  po zamknięciu doboru ustawień. Jeśli wynik testowy wpływa na dalsze
  strojenie, przestaje być niezależną oceną i trzeba to ujawnić.
- Raport obok modelu: ustawienia, seed, podział i wersja danych, liczności
  klas, wybrana epoka, wynik baseline przewidującego najczęstszą klasę
  treningową, accuracy i recall per klasa oraz macierz pomyłek
  (wiersze = klasy rzeczywiste, kolumny = przewidziane).
  Metadane eksperymentu nie muszą obciążać pliku modelu potrzebnego GUI.

Interfejs trenera przyjęty do implementacji B:

```powershell
dotnet run --project TrafficSign-Trainer -- "data" "models/signs.json" --epochs 30 --learning-rate 0.01 --hidden-size 64 --seed 42
```

Wyniki: `signs.json`, `signs.report.json` i `signs.split.json` obok siebie.
Parametry powyżej służą pierwszemu uruchomieniu, nie są dobranymi ustawieniami.
Zwykły przebieg ocenia tylko trening i walidację. Po ustaleniu ustawień
ocenić dokładnie zapisany model bez ponownego treningu:

```powershell
dotnet run --project TrafficSign-Trainer -- evaluate "models/signs.json" "data" "models/signs.split.json"
```

Ocena wymaga oryginalnego raportu treningowego i zapisuje `signs.test-report.json`.
Sprawdza hash modelu i fingerprint podziału/danych. Osobny tryb zastępuje
pierwotnie proponowane `--evaluate-test`, aby test nie wymagał retreningu.
Parser ma odrzucać nieznane opcje, brak wartości i parametry poza zakresem;
liczby w argumentach i JSON interpretować niezależnie od ustawień regionalnych.
Kod `0` oznacza zakończony przebieg i zapis wyników, `1` błąd. Istniejących
wyników domyślnie nie nadpisywać; użyć nowej nazwy. Sprawdzić kolizje wszystkich
trzech wyjść przed treningiem. Przygotować pliki tymczasowe obok celów,
opublikować raport i podział, a model jako ostatni znacznik ukończenia.
Po błędzie wycofać pliki opublikowane przez ten przebieg. Awaria procesu
może pozostawić pliki pomocnicze; nie mogą udawać kompletnego modelu.

Nie optymalizować macierzy, nie równoleglić uczenia i nie dodawać augmentacji,
dopóki pomiar na rzeczywistym zbiorze nie wykaże potrzeby.

### Minimalne GUI

Jeden ekran: wybór modelu, wybór zdjęcia, podgląd, przycisk rozpoznawania,
nazwa przewidzianej klasy i wynik modelu. Dodać czytelne komunikaty dla
brakującego modelu, złego JSON i uszkodzonego obrazu. Nie przedstawiać wyniku
softmax jako gwarantowanego prawdopodobieństwa poprawności.

Operacje blokujące wykonywać asynchronicznie, blokować powtórne kliknięcie
na czas pracy. Nie dodawać na tym etapie historii predykcji, galerii ani dashboardu.
Wybór pliku pozostaje na wątku UI; odczyt, dekodowanie i obliczenia nie mogą
blokować okna. Przy anulowaniu wyboru zachować poprzedni stan. Nowy model
zastępuje stary dopiero po walidacji; zmiana obrazu/modelu czyści stary wynik.
Przycisk predykcji jest dostępny tylko dla poprawnego modelu i obrazu, gdy
nie trwa operacja. Błąd odczytu pozwala ponowić wybór bez restartowania GUI.

Model wybiera jedną ze znanych klas także dla obrazu spoza zbioru. MVP nie
ma wytrenowanej klasy „brak znaku”; nie dodawać arbitralnego progu softmax
i nie obiecywać odrzucania dowolnych zdjęć. To ograniczenie opisać w GUI/README.

### Mała dokumentacja i celowane testy

README: rzeczywisty zakres, wymagania, układ danych, komendy treningu,
uruchomienie GUI, przykład modelu i wyniki wraz z opisem zbioru testowego.
Duży zbiór danych i wygenerowane modele trzymać poza Gitem; mały materiał
demonstracyjny można dołączyć, jeśli pozwalają na to warunki jego użycia.

Testować matematykę i granice danych: predykcję na znanych wagach, zgodność
gradientu z przybliżeniem numerycznym przy własnym backprop, zapis/odczyt
modelu, odrzucanie złych wymiarów, to samo przetwarzanie obrazu w obu aplikacjach
oraz rozłączność podziałów danych. GUI sprawdzić ręcznie na kilku obrazach
i błędnych plikach. Nie tworzyć testów dla samych getterów i konstruktorów.
Testy kontraktów dodawać wraz z implementacją, przed integracją GUI;
nie odkładać całej weryfikacji na końcowy etap.

## 6. Kolejność prac na `debloat`

Każdy etap kończyć sprawdzeniem swojego rezultatu i małym commitem.
Nie łączyć zmiany narzędzi budowania, namespace i matematyki w jednym commicie.
Checkbox zaznaczać dopiero po spełnieniu kryterium odbioru.

### Etap 0 — punkt odniesienia i środowisko (A)

- [ ] Zanotować stan Git i zachować zastane zmiany użytkownika, w tym README.
- [ ] Zapisać wersję SDK, odtworzone wersje pakietów, platformę i wariant GUI.
- [ ] Spróbować restore, build Debug/Release oraz uruchomienie obecnego GUI.
      Oddzielić błąd środowiska od błędu projektu; zapisać konkretny komunikat.
- [ ] Udokumentować aktualne zachowanie CLI na poprawnym JSON, pustym folderze,
      `[]`, `null`, uszkodzonym JSON i nieistniejącej ścieżce.

**Odbiór:** wiadomo, co działa przed zmianami i jak to sprawdzić ponownie.
Gdy środowisko nie pozwala uruchomić kompilatora, można poprawiać dokumentację,
ale nie uznawać usuwania konfiguracji WinUI za zweryfikowane.

### Etap 1 — odtwarzalne budowanie (A)

- [ ] Dodać `global.json`. Lokalnie wykryto SDK `10.0.401` oraz
      `11.0.100-rc.3.26522.148`; robocza konfiguracja dla zainstalowanego SDK 10:

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- [ ] Potwierdzić `dotnet --version` z katalogu repozytorium.
      `latestPatch` pozwala na nowsze poprawki tej samej linii `10.0.4xx`;
      nie wybierze automatycznie SDK 11 ani innego feature bandu.
      Docelowa wersja musi być dostępna także na komputerze demonstracyjnym.
- [ ] Przypiąć trzy bezpośrednie zależności GUI do konkretnych wersji,
      następnie wykonać restore i build. Lokalny `project.assets.json` jest
      źródłem kandydatów, a nie dowodem poprawnie działającego zestawu.
- [ ] Wyłączyć `PublishTrimmed` i `PublishReadyToRun` na czas MVP.
      Wrócić do nich tylko przy rzeczywistym problemie z rozmiarem lub startem.
- [ ] Wybrać jedną wspieraną ścieżkę uruchamiania GUI i rozstrzygnąć los
      ignorowanych `.pubxml`. Nie zmieniać jednocześnie modelu pakowania
      i wersji wszystkich zależności.

Proponowane polecenia kontrolne z katalogu repozytorium na Windows x64
(do wykonania przy realizacji, nie są potwierdzeniem buildu w tym przeglądzie):

```powershell
dotnet --version
dotnet restore TrafficSign-Si.slnx -p:Platform=x64
dotnet build TrafficSign-Si.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build TrafficSign-Si.slnx -c Release -p:Platform=x64 --no-restore
```

**Odbiór:** restore i oba buildy przechodzą, a okno uruchamia się w wybranym
wariancie. README zawiera sprawdzoną procedurę uruchomienia i wymagania Windows.
Weryfikacja nie może polegać wyłącznie na obecnym lokalnym `obj` i profilach.

### Etap 2 — właściwe porządki i zamknięcie debloat (A)

- [ ] Spłaszczyć katalog trenera i uporządkować namespace bez zmiany funkcji.
- [ ] Przenieść pustą zawartość `MainPage` do `MainWindow`, usunąć stronę,
      `RootFrame` i wywołanie nawigacji; sprawdzić start i pasek tytułowy.
- [ ] Usunąć nieużywane usingi i komentarze instruktażowe.
- [ ] Ograniczyć platformy do x64, jeśli potwierdzono taki zakres.
      Dostosować `.slnx`, `.csproj`, RID oraz pozostałe profile razem.
- [ ] Usuwać nieużywane deklaracje manifestu i zasoby pojedynczymi zmianami,
      każdorazowo sprawdzając build/start, a dla zasobów pakietu także pakowanie.
      Zachować potrzebne `runFullTrust`, konfigurację DPI i zgodności systemu.
- [ ] Skrócić reguły Git bez utraty ignorowania `bin`, `obj`, `.vs`, ustawień
      użytkownika, certyfikatów i dużych danych/modeli. Nie ignorować globalnie
      wszystkich JSON ani obrazów: modelowe fixture testów i ikony są potrzebne.
- [ ] Uzgodnić README z zachowaniem istniejącego CLI. Jeśli zostaje na dłużej,
      dodać małe kontrole pustego folderu i wag; przy bezpośrednim przejściu do B
      opisać stan faktyczny i zastąpić loader w etapie 3. Nie budować dla niego
      drugiego, przejściowego formatu modelu ani parsera podfolderów.

**Odbiór A:** mniejszy szkielet buduje się i uruchamia jak w etapie 1,
nie zależy od nieśledzonych ustawień, dokumentacja jest prawdziwa, a zmiany
nie sugerują, że istnieje już trening lub rozpoznawanie. Tutaj można zamknąć
sam debloat i przejść do osobnego zestawu commitów MVP.

### Etap 3 — dane, wspólne wejście i kontrakt modelu (B)

Warunek wejścia: ustalone wymagania i konkretny zbiór z sekcji 3.

- [ ] Dodać `TrafficSign.Core`, referencje obu aplikacji oraz projekt testowy.
- [ ] Wybrać jeden dekoder i przypiąć jego wersję. Sprawdzić formaty, warunki
      użycia i działanie w obu projektach na kilku rzeczywistych plikach.
- [ ] Zaimplementować `ImagePreprocessor` i model v1 zgodnie z sekcją 5.
- [ ] W trenerze zastąpić obecny odczyt wag wejściem danych z etykietami;
      zinwentaryzować klasy i grupy, utworzyć podział i zapisać jego opis.
- [ ] Dodać testy preprocessingu, walidacji modelu i rozłączności danych.

**Odbiór:** ten sam obraz daje ten sam wektor wejściowy w obu aplikacjach;
zapis/odczyt zachowuje parametry i kolejność klas; błędne wymiary i format
są odrzucane przed predykcją. Podział jest powtarzalny dla niezmienionych
danych i nie zawiera wspólnych plików, duplikatów ani znanych grup.

### Etap 4 — mały działający przepływ obliczeń (B)

- [ ] Zaimplementować forward MLP, stabilny softmax i stratę, następnie backprop
      oraz aktualizację SGD. Bez ogólnego silnika dowolnych warstw.
- [ ] Sprawdzić forward na ręcznie ustalonych wagach i oczekiwanym wyniku.
- [ ] Sprawdzić gradient różnicą centralną na maleńkiej sieci w `double`,
      obejmując obie macierze wag i biasy. Tolerancję ustalić dla tego testu;
      nie opierać oceny tylko na błędzie względnym przy gradiencie bliskim zeru.
- [ ] Celowo nauczyć sieć kilku stałych przykładów, aby potwierdzić, że strata
      maleje i sieć potrafi je zapamiętać. To test implementacji, nie skuteczności.
- [ ] Zapisać model i wykonać z niego predykcję po ponownym odczycie.

**Odbiór:** test gradientu przechodzi, mały trening działa, a predykcje przed
i po zapisie są zgodne w ustalonej tolerancji. Zanim powstanie kompletne GUI,
istnieje sprawdzony przepływ obraz → sieć → zapis → odczyt → etykieta.

### Etap 5 — trening i ocena na rzeczywistych danych (B)

- [ ] Dodać docelowe argumenty CLI, kontrolę parametrów, przetasowanie treningu
      i raport straty/accuracy dla treningu i walidacji na każdą epokę.
- [ ] Zachować najlepszą epokę według walidacji i zapisać model oraz raport.
- [ ] Ustalić rozsądną wielkość eksperymentu na podstawie czasu i pamięci
      pierwszego przebiegu; nie wczytywać bez ograniczeń całego dużego zbioru.
- [ ] Porównać wynik z baseline większościowej klasy. Sprawdzić pomyłki per
      klasa; gdy obraz w szarości gubi istotne różnice, porównać RGB na walidacji.
- [ ] Po zamknięciu doboru ustawień uruchomić `evaluate` dla zapisanego modelu
      i podziału; podać końcowy wynik i zachować konfigurację eksperymentu.

**Odbiór:** trener kończy przebieg z poprawnym modelem i raportem, walidacja
steruje wyborem parametrów, a test jest odłożony. Wynik niespełniający wymagań
jest sygnałem do sprawdzenia danych/metody, nie do zmiany podziału pod wynik.
Powtarzalność oznacza ten sam podział i zbliżony wynik w zapisanym środowisku;
nie obiecywać identycznych bitowo wag na każdym komputerze.

### Etap 6 — integracja jednego ekranu GUI (B)

- [ ] Dodać wybór modelu, obrazu, podgląd, predykcję i etykietę ze score.
- [ ] Wykorzystać `Core` i stan operacji opisany w sekcji 5.
- [ ] Ręcznie sprawdzić: start bez modelu, anulowanie wyboru, uszkodzony JSON,
      złą wersję i wymiary modelu, uszkodzony obraz, poprawny wynik, zmianę
      modelu/obrazu po wyniku i wielokrotne kliknięcie podczas pracy.
- [ ] Porównać wynik GUI z bezpośrednim wywołaniem `Core` dla tych samych plików.

**Odbiór:** GUI pokazuje ten sam wynik co wspólna logika, pozostaje responsywne
i pozwala odzyskać poprawny stan po błędzie bez ponownego uruchomienia.

### Etap 7 — odtworzenie demonstracji i odbiór MVP (B)

- [ ] Uzupełnić README o działające komendy, format danych, źródło zbioru,
      wersje środowiska, wyniki, ograniczenia i sposób uzyskania modelu demo.
- [ ] Dołączyć małe fixture testowe; materiał demonstracyjny tylko wtedy,
      gdy wolno go udostępniać. Dane, wyniki i modele generowane ignorować
      przez konkretne katalogi, np. `data/`, `models/`, `runs/`.
- [ ] Uruchomić `dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj`
      oraz buildy Debug/Release. Wykonać publikację/pakowanie
      wybraną ścieżką i uruchomić jej wynik, jeśli tak będzie oddawana aplikacja.
- [ ] Na nowym komputerze lub w czystym katalogu pobrać repozytorium bez
      lokalnych `bin`, `obj`, `.vs` i ignorowanych profili, odtworzyć dane
      według README, wytrenować model i użyć go w GUI.
- [ ] Usunąć dopiero teraz kod, który faktycznie pozostał nieużywany.

**Odbiór B:** osoba korzystająca z README potrafi wytrenować model, wczytać go
w GUI i sklasyfikować pojedynczy wykadrowany znak. Raport podaje niezależny
wynik i warunki pomiaru; wymagania prowadzącego z sekcji 3 są spełnione.

## 7. Ryzyka i granice rozbudowy

| Ryzyko | Działanie i moment decyzji |
| --- | --- |
| Zadanie wymaga detekcji na zdjęciu ulicy lub innej metody | Wyjaśnić przed B; zmienić zakres, zamiast budować niepasujące MVP. |
| Zbyt mało danych lub powiązane serie w różnych częściach | Sprawdzić przed treningiem; poprawić dane/podział i opisać ograniczenia. |
| MLP na pikselach ma słaby wynik | Najpierw sprawdzić gradient, preprocessing, etykiety i baseline; dopiero pomiary uzasadniają zmianę reprezentacji lub modelu. |
| Porządki psują WinUI lub dystrybucję | Zmieniać konfigurację małymi krokami od sprawdzonego punktu odniesienia. |
| Rosnący zakres „przy okazji” | Kamerę, detekcję, augmentację, GPU, rozbudowany MVVM i optymalizacje zostawić jako osobne zadania po B. |

Nie ustalać czasu realizacji całego MVP przed wyborem danych i pierwszym
poprawnym treningiem. Najpierw oszacować A, a po etapie 4 pozostałą część B.

## 8. Zakres wcześniejszego przeglądu dokumentu

Wcześniejsza aktualizacja zmieniała wyłącznie ten plan. Nie zmieniano wtedy kodu aplikacji,
konfiguracji projektów ani zastanych zmian użytkownika w README.
Potwierdzono przeglądem plików stan szkieletu, rozbieżności README/kodu,
obecność ignorowanych profili publikacji i zainstalowane wersje SDK.
Nie wykonano treningu ani nie potwierdzono działania GUI.

Poprzednia wersja dokumentu odnotowała próby kompilacji obu projektów bez
restore, dla GUI z platformą x64. Według tego zapisu również poza piaskownicą
kompilację zatrzymał brak dostępu do potoków `\\.\pipe\LOCAL\dotnet_*`:
w trenerze przy Roslyn, w GUI przy kompilatorze XAML; wyłączenie shared
compilation nie pomogło. W ramach aktualizacji planu nie powtarzano buildów.
Ten historyczny błąd pozostaje do sprawdzenia w etapie 0, nie stanowi dowodu
na wadę kodu ani na jego poprawność.

## 9. Odniesienia techniczne

- Dobór SDK, `allowPrerelease` i znaczenie `latestPatch`:
  [Microsoft: global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
- `PublishTrimmed` działa podczas publikacji i wpływa także na analizę
  przy buildzie: [Microsoft: Trimming options](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options).

## 10. Realizacja po recenzji subagenta

Recenzent potwierdził zasadność A/B i wskazał brak kontraktu manifestu,
potrzebę oceny już zapisanego modelu, spójność identyfikatora preprocessingu
z wymiarami oraz publikowanie kompletu wyników z modelem jako ostatnim plikiem.
Powyższe poprawki włączono do planu i implementacji.

Prace podzielono między wykonawcę Core/Trainer/Tests a integrację
konfiguracji, GUI i dokumentacji. Niezależny recenzent sprawdził także gotowy
kod: nie znalazł nowych istotnych błędów. Jego uwagę o ogłaszaniu wyniku
czytnikom ekranu poprawiono przez live region i zdarzenie UI Automation.

- Punkt odniesienia: oba pierwotne projekty zbudowane bez restore,
  SDK 11 preview, GUI x64, `UseSharedCompilation=false`; 0 ostrzeżeń/błędów.
  Historyczny błąd potoków z sekcji 8 nie powtórzył się.
- SDK 10.0.401 wybierany przez nowy `global.json`; wersje pakietów przypięte,
  zależności przechodnie utrwalane w `packages.lock.json`.
- GUI x64 bez zależności od lokalnych profili, jeden ekran zamiast nawigacji,
  usunięte deklaracje PhoneIdentity/systemAIModels, uproszczone reguły Git.
- Nowe GUI z Core zbudowane w Debug bez ostrzeżeń/błędów i uruchomione
  przez WinApp jako packaged. Początkowy ekran sprawdzony wizualnie,
  predykcja bez danych jest niedostępna. Sterowanie UI przerwano klawiszem
  Escape; nie wykonano pełnego ręcznego scenariusza pickerów i predykcji.
- Dodano generator 48 własnych syntetycznych PNG horizontal/vertical
  w `tools/New-DemoDataset.ps1`. To test przepływu, nie zbiór znaków drogowych.
- Wdrożono MLP z backprop/SGD, walidowany model v1, wspólny preprocessing,
  manifest grup i podziałów, raporty i osobną ocenę zapisanej sieci.
- `dotnet restore TrafficSign-Si.slnx -p:Platform=x64 --locked-mode` przeszedł.
- Release całej solucji: build bez ostrzeżeń i błędów.
- `dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj -c Release --no-build --no-restore`:
  **26/26 testów**, bez pominięć. Obejmują wszystkie gradienty, zapamiętanie
  małej próbki, ekstremalne logity, model, EXIF/alpha/bilinear, grupy,
  duplikaty, oficjalny podział, błędy CLI i raportów oraz pełny trening/ocenę.
- Przebieg techniczny zgodny z README: 48 sztucznych obrazów, 10 epok,
  hidden-size 8, seed 42. Wybrana epoka 10, validation loss 0.108585;
  osobne `evaluate`: 8 obrazów, loss 0.110907, accuracy 100%, baseline 50%.
  Wynik dotyczy wyłącznie poziomych/pionowych wzorów, nie znaków drogowych.
  Artefakty pozostają lokalnie w ignorowanym `artifacts/demo/`.
- Czysta kopia źródeł w `artifacts/clean-verification/`, bez `bin`, `obj`,
  lokalnych profili publikacji i ustawień użytkownika: locked restore oraz
  build Debug całej solucji przeszły bez ostrzeżeń/błędów. To weryfikacja na
  tym samym komputerze i z jego cache NuGet, nie test drugiego komputera.
- `dotnet publish` Release z `GenerateAppxPackageOnBuild=true`,
  `AppxPackageSigningEnabled=false` i `AppxBundle=Never` utworzył kontrolny
  MSIX. Sprawdzono obecność Core, SkiaSharp, natywnego `libSkiaSharp.dll`,
  ikony i manifestu w pakiecie. Nie instalowano ani nie uruchamiano tego
  niepodpisanego pakietu; start potwierdzono dla lokalnej tożsamości debug.
- Końcowe buildy solucji działały poza sandboxem, ponieważ wewnątrz MSBuild
  dwukrotnie kończył się „Build FAILED” bez ostrzeżeń ani diagnostyki błędu.
  Te same polecenia poza sandboxem przeszły; nie ukrywano błędu kompilacji kodu.

Do odbioru na prawdziwych danych nadal potrzebne są: wskazany zbiór i jego
metadane, wymagania zadania oraz rzeczywisty pomiar skuteczności. Nie uznawać
ich za wykonane na podstawie samych testów syntetycznych.
