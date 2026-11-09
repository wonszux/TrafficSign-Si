 # Raport zmian TrafficSign-Si

Data: 9 listopada 2026. Gałąź: `debloat`. Zmiany pozostają niecommitowane.

Raport opisuje wykonane porządki i techniczne MVP na podstawie bieżących plików,
różnic Git, [README](../README.md) oraz [planu](PLAN-DEBLOAT.md).
Autor raportu przeprowadził analizę statyczną; nie uruchamiał ponownie buildów,
testów ani automatyzacji GUI. Wyniki wykonania podane dalej pochodzą z sesji
implementacyjnej i jej zapisu w sekcji 10 planu. README było zmodyfikowane już
przed implementacją, więc całego jego diffu względem HEAD nie przypisujemy
wyłącznie nowej pracy. Statystyka `git diff` nie obejmuje nowych plików
nieśledzonych; poniższa mapa uwzględnia także te pliki.

## 1. Cel i rezultat

Prace rozdzielono na A — uporządkowanie szkieletu i konfiguracji — oraz B —
implementację przepływu danych, treningu, modelu i predykcji. Zaimplementowano
A i techniczne MVP B. Ocena skuteczności na rzeczywistych znakach oraz pełny
ręczny odbiór GUI pozostają do wykonania.

| Obszar | Przed zmianami | Po zmianach |
| --- | --- | --- |
| Trener | Liczył ścieżki obrazów i wczytywał istniejącą tablicę wag. | Dekoduje dane z etykietami, dzieli je, trenuje MLP i zapisuje wyniki. |
| Sieć | Klasa przechowująca publiczne `readonly double[]`. | Forward, strata, gradienty, backprop i SGD; własność parametrów po stronie sieci. |
| Model | Luźna tablica bez architektury i etykiet. | Walidowany JSON v1 z parametrami, preprocessingiem i kolejnością klas. |
| GUI | Pusta strona w `Frame`. | Jeden ekran wyboru modelu i obrazu, podgląd, predykcja, wynik i błędy. |
| Wspólny kod | Brak połączenia między projektami. | Trener i GUI korzystają z `TrafficSign.Core`. |
| Budowanie | SDK wybierane z systemu, pakiety GUI `*`, kilka platform. | SDK 10, przypięte pakiety i locki, GUI x64, sprawdzony restore i build. |
| Weryfikacja | Brak projektu testowego. | 26 przypadków testowych i syntetyczny przebieg trening–zapis–ocena. |

Zakres aplikacji to klasyfikacja jednego wykadrowanego znaku. Nie dodano
detekcji obiektów na zdjęciu ulicy, kamery, CNN, GPU, serwera ani bazy danych.

## 2. SDK, zależności i konfiguracja

- [global.json](../global.json) wybiera SDK `10.0.401`, `latestPatch` i
  `allowPrerelease=false`. Dopuszcza poprawki tej samej linii `10.0.4xx`.
- [Directory.Build.props](../Directory.Build.props) włącza generowanie
  `packages.lock.json` we wszystkich czterech projektach. Locki zapisują także
  zależności przechodnie; restore kontrolowano przez `--locked-mode`.
- GUI zachowuje target `net10.0-windows10.0.26100.0`; Core, trener i testy
  celują w `net10.0`. Namespace ujednolicono do `TrafficSign.*`.
- W solucji i projekcie GUI pozostawiono x64. `PlatformTarget=x64` oraz
  `RuntimeIdentifier=win-x64` nie zależą już od architektury procesu builda.
- `SelfContained=true` zapisano w projekcie. Usunięto warunkowe odwołanie do
  lokalnego profilu publikacji; ignorowane `.pubxml` pozostały na dysku.
- Wyłączono `PublishTrimmed` i `PublishReadyToRun`. Zachowano obsługę WinUI,
  MSIX oraz wymagane elementy integracji narzędzi pakujących.

| Projekt | Bezpośrednie zależności NuGet |
| --- | --- |
| Detector | `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2957; `Microsoft.WindowsAppSDK` 2.6.5; `Microsoft.Windows.SDK.BuildTools.WinApp` 0.7.1. |
| Core | `SkiaSharp` 3.119.4, wybrane jako wspólny dekoder z licencją MIT. |
| Tests | `Microsoft.NET.Test.Sdk` 17.14.1; `xunit` 2.9.3; `xunit.runner.visualstudio` 3.1.5. |
| Trainer | Referencja do Core; bez osobnej biblioteki ML lub parsera CLI. |

W manifeście pakietu usunięto `PhoneIdentity`, `systemAIModels` i ich zbędne
namespace. Zachowano `runFullTrust`, identyfikację pakietu i używane grafiki.
Usunięto tylko wpis `Content` dla `LockScreenLogo.scale-200.png`; sam plik
pozostaje w repozytorium. Nie usuwano wszystkich ikon ani lokalnych profili.

Z `launchSettings.json` usunięto wariant unpackaged. Profil `MsixPackage`
pozostaje dla Visual Studio; README podaje `--no-launch-profile` dla CLI,
ponieważ uruchomienie packaged obsługuje w nim WinApp.

`.gitignore` skrócono do reguł projektu: buildy, ustawienia użytkownika,
certyfikaty, profile, raporty testów oraz katalogi danych i wyników.
Nie ignoruje globalnie wszystkich obrazów ani JSON. `.gitattributes` określa
tekstowe pliki źródłowe, binarne grafiki i CRLF dla skryptów PowerShell.

## 3. Architektura i kontrakty

Zależności wykonawcze: `Trainer → Core` oraz `Detector → Core`.
Projekt `Tests` odwołuje się do Core i trenera. Core nie zna konsoli ani XAML.
Code-behind odpowiada za obsługę ekranu, a obliczenia są we wspólnej bibliotece.
Nie wprowadzono MVVM, kontenera DI ani interfejsów dla każdej klasy.

### Sieć neuronowa

[NeuralNetwork.cs](../TrafficSign.Core/NeuralNetwork.cs) implementuje układ
`1024 → H → K`: 32×32 piksele, jedna warstwa `tanh`, wyjście softmax.
Wagi są inicjalizowane metodą Xavier, biasy zerami, a uczenie używa SGD
po jednej próbce. Przetasowywany jest wyłącznie zbiór treningowy.

Softmax odejmuje największy logit przed potęgowaniem. Cross-entropy jest
liczona w przestrzeni przesuniętych logitów; dzięki temu równe wartości
rzędu `1e16` nie gubią składnika `log(2)` przez odejmowanie dużych liczb.
Backprop wyznacza gradienty obu macierzy i biasów przed aktualizacją wag.
Nieskończone parametry lub wynik obliczeń przerywają przebieg.

Konstruktor kopiuje etykiety i parametry z DTO. `GetModel()` zwraca nową kopię,
więc zmiana eksportowanych tablic nie zmienia działającej sieci.

### Wspólne przetwarzanie obrazu

[ImagePreprocessor.cs](../TrafficSign.Core/ImagePreprocessor.cs) sprawdza
rozszerzenie oraz rzeczywisty format JPEG/PNG. Odrzuca animacje, błędy
dekodowania, nieprawidłowe wymiary i obrazy powyżej 20 milionów pikseli.
Procedura `gray32-v1` uwzględnia EXIF, kompozycję alpha na białym tle,
biliniowe próbkowanie środków pikseli i zmianę rozmiaru do 32×32.
Szarość to `(0.299R + 0.587G + 0.114B) / 255`; wynik jest spłaszczany
wierszami bez pośredniego zaokrąglania do bajtów. Liniowe przeliczenie
szarości jest połączone z interpolacją. GUI używa wektora z oryginalnego
pliku, a nie pikseli kontrolki podglądu.

### Model i zapis plików

[ModelData.cs](../TrafficSign.Core/ModelData.cs) wymaga `formatVersion=1`,
`gray32-v1`, wymiarów 32×32 i jednego kanału. Weryfikuje także `H` 1–2048,
2–1000 niepustych unikalnych etykiet, długości tablic i skończoność liczb.
Tablice mają długości `H×1024`, `H`, `K×H`, `K`. Format nie obsługuje starej
luźnej tablicy wag; brakuje w niej informacji potrzebnych do migracji.

[ModelFile.cs](../TrafficSign.Core/ModelFile.cs) używa JSON camelCase,
odrzuca pliki powyżej 128 MiB i waliduje model po odczycie oraz przed zapisem.
Zapis tworzy plik tymczasowy obok celu, po czym przenosi go bez nadpisywania.
Trener sprawdza kolizje wszystkich trzech wyjść przed uczeniem i publikuje
podział oraz raport przed modelem. Model jest znacznikiem ukończenia.
Przy obsłużonym błędzie usuwa pliki opublikowane przez dany przebieg.
Nie jest to transakcja odporna na nagłą awarię procesu lub systemu:
mogą pozostać pliki pomocnicze, ale nie kompletnie wyglądający model.

## 4. Dane, podział i trening

[Dataset.cs](../TrafficSign-Trainer/Dataset.cs) przyjmuje `data/<klasa>/<obraz>`.
W klasach nie są dozwolone dalsze katalogi. Nieobsługiwane pliki są liczone
jako pominięte, puste klasy i uszkodzone obrazy są błędami. Etykiety i pliki
sortowane są porządkiem ordinal. Limit 20 000 obrazów ogranicza przygotowanie
wszystkich wektorów w pamięci: około 156 MiB samych `double[]` plus narzuty.

Każdy plik otrzymuje SHA256. Identyczne kopie i podane grupy są łączone
w jednostki podziału, także przez powiązania przechodnie. Kopie nie mogą
mieć sprzecznych etykiet ani przekraczać części zbioru. Grupa obejmuje jedną
klasę. Bez manifestu losowane są całe jednostki przy stałym seedzie,
roboczo 70/15/15; proporcje dotyczą grup, niekoniecznie liczby zdjęć.
Każda klasa musi występować w train, validation i test. Przy automatycznym
podziale potrzebuje przynajmniej trzech niezależnych jednostek.

Opcjonalny `--split-manifest` opisuje wszystkie obrazy dokładnie raz:
`formatVersion`, `entries`, względne `path`, `split`, opcjonalne `groupId`
i `sha256`. Przypisania są kompletne albo wszystkie pozostają `null`.
Obsługiwane części to `train`, `validation`, `test`; oficjalny train/test
zachowuje test, wydzielając walidację wyłącznie z treningu. Raport wskazuje
brak pełnych metadanych grup, jeśli nie podano ich dla wszystkich obrazów.
Hash wykrywa identyczne kopie; nie rozpoznaje podobnych ujęć tego samego znaku.

[Training.cs](../TrafficSign-Trainer/Training.cs) raportuje stratę i accuracy
treningu oraz walidacji po każdej epoce. Zachowuje najmniejszą stratę
walidacyjną; przy remisie wcześniejszą epokę. Test nie steruje wyborem modelu.
Raport zawiera parametry, seed, historię, liczności klas, najlepszą epokę,
baseline najczęstszej klasy treningowej, metryki i ograniczenia grupowania.
Fingerprint zbioru wiąże uporządkowane ścieżki, indeksy etykiet, hashe,
grupy oraz przypisania. Powtarzalność nie oznacza identycznych bitowo wag
na każdym środowisku sprzętowym i wersji bibliotek.

## 5. CLI i wyniki

```powershell
dotnet run --project TrafficSign-Trainer -- "data" "models/signs.json" --epochs 30 --learning-rate 0.01 --hidden-size 64 --seed 42
dotnet run --project TrafficSign-Trainer -- evaluate "models/signs.json" "data" "models/signs.split.json"
```

| Opcja | Domyślnie | Kontrola |
| --- | --- | --- |
| `--epochs` | 30 | 1–10000. |
| `--learning-rate` | 0.01 | Liczba skończona, >0 i ≤10; kropka dziesiętna. |
| `--hidden-size` | 64 | 1–2048. |
| `--seed` | 42 | Liczba całkowita `Int32`. |
| `--split-manifest` | Brak | Ścieżka do kompletnego manifestu danych. |

Parser odrzuca nieznane/powtórzone opcje, brak wartości i błędne zakresy.
`--help` lub `-h` daje pomoc i kod 0; błędy argumentów, danych, JSON lub
plików dają komunikat na stderr i kod 1. Trening tworzy `signs.json`,
`signs.report.json` i `signs.split.json`, bez nadpisywania istniejących plików.

`evaluate` wymaga zapisanego modelu, dokładnego podziału i oryginalnego
raportu treningowego obok modelu. Sprawdza etykiety, hashe plików, hash modelu
i fingerprint zbioru; nie trenuje ponownie. Ocenia tylko test i zapisuje
`signs.test-report.json`: accuracy, strata, recall per klasa, baseline
i macierz pomyłek. Wiersze macierzy oznaczają klasy rzeczywiste, kolumny
przewidziane. Zły typ lub brak pól raportu są obsłużonym błędem CLI.

## 6. GUI i dostępność

[MainWindow.xaml](../TrafficSign-Detector/MainWindow.xaml) zastępuje pustą
stronę. Zawiera wybór modelu i obrazu, ścieżki plików, podgląd, przycisk
predykcji, wskaźnik pracy, InfoBar oraz nazwę klasy i wynik softmax.
Zachowano pasek tytułowy i ikonę; początkowy rozmiar okna to 920×900.

Pickery są inicjalizowane HWND okna. Odczyt modelu, preprocessing i predykcja
korzystają z `Task.Run`; wybór pliku i aktualizacja kontrolek zostają na UI.
Stan busy blokuje wszystkie przyciski. Predykcja jest dostępna tylko przy
poprawnym modelu i wektorze obrazu. Nowy plik zastępuje poprzedni dopiero
po poprawnym odczycie; obraz także po załadowaniu podglądu. Anulowanie
wyboru zachowuje stan, poprawna zmiana czyści wynik, a błędy trafiają do InfoBar.

Kontrolki interaktywne mają nazwy automatyzacji. `ResultLabel` ma live region
`Polite`; po predykcji kod podnosi `LiveRegionChanged` na automation peer.
To implementacja wsparcia dostępności, nie potwierdzony test Narratora.
Ekran wyjaśnia ograniczenia softmax i brak klasy „brak znaku”.

## 7. Mapa plików

| Status | Pliki lub grupa | Cel |
| --- | --- | --- |
| Dodane | `global.json`, `Directory.Build.props` | Dobór SDK i generowanie locków. |
| Zmienione | `.gitignore`, `.gitattributes`, `TrafficSign-Si.slnx` | Reguły projektu, x64 i cztery projekty w solucji. |
| Dodane | `TrafficSign.Core/TrafficSign.Core.csproj`, `ImagePreprocessor.cs`, `NeuralNetwork.cs`, `ModelData.cs`, `ModelFile.cs` | Wspólne wejście, sieć i kontrakt modelu. |
| Dodane | `TrafficSign-Trainer/Program.cs`, `Dataset.cs`, `Training.cs` | Parser, podziały i orkiestracja eksperymentu. |
| Usunięte | `TrafficSign-Trainer/src/Program.cs`, `src/trainer/NeuralNetwork.cs` | Zastąpienie pierwotnego loadera i klasy-tablicy. |
| Zmieniony | `TrafficSign-Trainer/TrafficSign-Trainer.csproj` | Namespace i referencja do Core. |
| Zmienione | `TrafficSign-Detector/App.xaml`, `App.xaml.cs` | Spójny namespace i uproszczony start. |
| Zmienione | `TrafficSign-Detector/MainWindow.xaml`, `MainWindow.xaml.cs` | Kompletny ekran klasyfikacji i jego obsługa. |
| Usunięte | `TrafficSign-Detector/MainPage.xaml`, `MainPage.xaml.cs` | Usunięcie pustej strony i nawigacji. |
| Zmienione | `TrafficSign-Detector/TrafficSign-Detector.csproj`, `Package.appxmanifest`, `Properties/launchSettings.json` | Platforma, pakiety, Core i packaged jako podstawowa ścieżka. |
| Dodane | `TrafficSign.Tests/TrafficSign.Tests.csproj`, `NetworkTests.cs`, `PreprocessingTests.cs`, `DatasetTests.cs`, `TrainerTests.cs`, `TestFiles.cs` | Testy i własne generowane fixture. |
| Dodane | `packages.lock.json` w każdym z czterech projektów | Utrwalenie grafu zależności. |
| Dodany | `tools/New-DemoDataset.ps1` | Powtarzalny generator 48 technicznych wzorów PNG. |
| Dokumentacja | `README.md`, `docs/PLAN-DEBLOAT.md`, niniejszy raport | Instrukcje, kontrakty, plan, dowody i ograniczenia. |

Wygenerowane dane, modele, czysta kopia kontrolna i wyniki publikacji pozostają
lokalne w ignorowanych katalogach. Nie są materiałem źródłowym aplikacji.

## 8. Testy i dowody wykonania

Zestaw zawiera **19 metod testowych i 26 przypadków uruchomienia**.
Różnica wynika z wariantów `Theory`, a nie z dodatkowych testów GUI.

| Plik | Przypadki | Sprawdzane zachowanie |
| --- | --- | --- |
| `NetworkTests.cs` | 6 | Znany forward i własność tablic; loss przy logitach 1e16; numeryczny gradient wszystkich wag i biasów; zapamiętanie dwóch próbek; round-trip i błędny model; wymiary preprocessingu/wejścia. |
| `PreprocessingTests.cs` | 4 | Alpha i powtarzalność; bilinear oraz kolejność wierszy; EXIF obrót JPEG; błędne i nieobsługiwane obrazy. |
| `DatasetTests.cs` | 4 | Determinizm i grupy; oficjalny test/walidacja i zmiana hasha; duplikaty przez podziały/etykiety; za mało danych i niepełny manifest. |
| `TrainerTests.cs` | 12 | Pełny eksperyment i kolizje (1); złe raporty treningowe (4); złe opcje (5); uszkodzony obraz bez modelu (1); pomoc/brak/powtórzenie wartości (1). |

W sesji implementacyjnej wykonano:

```powershell
dotnet restore TrafficSign-Si.slnx -p:Platform=x64 --locked-mode
dotnet build TrafficSign-Si.slnx -c Release -p:Platform=x64 -p:UseSharedCompilation=false --no-restore
dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj -c Release --no-build --no-restore
dotnet restore artifacts/clean-verification/TrafficSign-Si.slnx -p:Platform=x64 --locked-mode
dotnet build artifacts/clean-verification/TrafficSign-Si.slnx -c Debug -p:Platform=x64 -p:UseSharedCompilation=false --no-restore
dotnet publish TrafficSign-Detector/TrafficSign-Detector.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never --no-restore
```

Locked restore i oba buildy przeszły; buildy miały 0 ostrzeżeń i błędów.
Testy Release: **26 passed, 0 failed, 0 skipped**. Czysta kopia nie zawierała
`bin`, `obj`, lokalnych profili ani ustawień użytkownika; używała tego samego
komputera i cache NuGet, więc nie zastępuje próby na innym komputerze.
Wcześniejszy baseline obu pierwotnych projektów także zbudowano poprawnie
na SDK 11 preview. Końcowe buildy solucji powiodły się poza sandboxem po
nieudanych próbach wewnątrz, które nie zwróciły diagnostyki błędu kompilacji.

Publikacja utworzyła niepodpisany kontrolny MSIX. W archiwum potwierdzono
Core, SkiaSharp, `libSkiaSharp.dll`, ikonę i manifest. Pakietu nie instalowano
ani nie uruchamiano. GUI uruchomiono przez lokalną tożsamość debug WinApp:
potwierdzono początkowy wygląd i niedostępną predykcję bez danych. Dalsze
sterowanie przerwał fizyczny Escape; pełna predykcja w GUI nie jest potwierdzona.

## 9. Demonstracja i dalszy odbiór

```powershell
.\tools\New-DemoDataset.ps1
dotnet run --project TrafficSign-Trainer -- "artifacts/demo-data" "artifacts/demo/signs.json" --epochs 10 --hidden-size 8
dotnet run --project TrafficSign-Trainer -- evaluate "artifacts/demo/signs.json" "artifacts/demo-data" "artifacts/demo/signs.split.json"
dotnet run --project TrafficSign-Detector --no-launch-profile -p:Platform=x64
```

Skrypt tworzy 48 własnych wzorów horizontal/vertical. Przebieg z seed 42
wybrał epokę 10: validation loss 0.108585. Osobna ocena ośmiu obrazów dała
loss 0.110907, accuracy 100% i baseline 50%. **To wynik rozróżniania sztucznych
wzorów, nie skuteczność rozpoznawania znaków drogowych.** Artefakty są
w `artifacts/demo/`. Przy powtórzeniu należy wybrać nowe ścieżki, ponieważ
generator i trener odrzucają istniejące wyjścia.

Względem początkowego planu dodano manifest wejściowy grup/oficjalnych części,
zastąpiono `--evaluate-test` osobnym `evaluate`, doprecyzowano zapis kompletu
wyników i dodano locki. Wprowadzono je po recenzji dla spójności eksperymentu.
Nazwy katalogów projektów pozostawiono; uproszczono namespace i strukturę
trenera. Zrezygnowano z rozwijania tymczasowego loadera starych wag.

Do zakończenia odbioru pozostają: wybór rzeczywistego zbioru i metadanych
serii, potwierdzenie wymagań prowadzącego, trening oraz niezależny pomiar
na znakach, ręczny scenariusz GUI z błędnymi plikami i zmianą modelu,
sprawdzenie Narratora oraz uruchomienie na komputerze demonstracyjnym.
RGB wymaga osobnego preprocessingu i nowego treningu; nie wdrożono go bez
pomiaru potrzeby. Dystrybucja instalowalnego MSIX wymaga konfiguracji podpisu.
Nie ma dotąd podstaw do deklarowania wymaganego procentu skuteczności,
niezawodnej klasy „brak znaku” ani pełnego odbioru rzeczywistego zadania.
