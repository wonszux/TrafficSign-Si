# TrafficSign-Si

Klasyfikacja zdjęcia jednego, już wykadrowanego znaku. Trener uczy własną sieć
MLP (`1024 → warstwa tanh → softmax`) na etykietach z katalogów, zapisuje model
JSON, a aplikacja WinUI wczytuje go i rozpoznaje wybrane zdjęcie.

Projekt nie wyszukuje znaków na zdjęciach ulicy, nie obsługuje kamery ani klasy
„brak znaku”. Softmax jest wynikiem modelu, a nie gwarancją poprawności.

## Budowanie i uruchamianie

Wymagania: Windows x64, .NET SDK `10.0.401` (lub nowsza poprawka linii
`10.0.4xx`), dostęp do NuGet przy pierwszym restore. SDK wybiera `global.json`.
GUI używa Windows App SDK i lokalnej tożsamości pakietu debug. Pierwsze
uruchomienie rejestruje ją przez narzędzie WinApp; nie potrzebuje lokalnych
profili `.pubxml`. W Visual Studio wybierz konfigurację x64 i projekt Detector.

```powershell
dotnet restore TrafficSign-Si.slnx -p:Platform=x64 --locked-mode
dotnet build TrafficSign-Si.slnx -c Debug -p:Platform=x64 --no-restore
dotnet build TrafficSign-Si.slnx -c Release -p:Platform=x64 --no-restore
dotnet test TrafficSign.Tests/TrafficSign.Tests.csproj
dotnet run --project TrafficSign-Detector --no-launch-profile -p:Platform=x64
```

Pakiety bezpośrednie mają konkretne wersje, a `packages.lock.json` utrwala także
zależności przechodnie. Przy świadomej zmianie zależności odtwórz i sprawdź locki.
`--no-launch-profile` omija profil Visual Studio typu `MsixPackage`, którego
samo .NET CLI nie obsługuje; aplikację nadal uruchamia WinApp jako packaged.

Sprawdzono także generowanie kontrolnego pakietu MSIX:

```powershell
dotnet publish TrafficSign-Detector -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never
```

Wynik jest w `TrafficSign-Detector/AppPackages/`. To pakiet bez podpisu
do kontroli zawartości; do pracy lokalnej użyj powyższego `dotnet run`.
Dystrybucja instalowalnego pakietu wymaga skonfigurowania podpisu.

## Dane i trening

Jeden katalog na klasę, bez dalszego zagnieżdżania:

```text
data/
  stop/
    001.png
    002.jpg
  ustap-pierwszenstwa/
    001.png
    002.jpg
```

Obsługiwane są statyczne JPEG/PNG do 20 milionów pikseli. Każdy obraz jest
rzeczywiście dekodowany; uszkodzony plik przerywa przygotowanie danych.
Wymagane są co najmniej dwie klasy i trzy niezależne obrazy/grupy każdej klasy,
aby każda miała część treningową, walidacyjną i testową. Trzy przykłady to minimum
techniczne, zbyt mało do wiarygodnego pomiaru skuteczności. Dla pierwszego
eksperymentu wybierz 5–10 klas i znacznie więcej przykładów. Limit to 20 000
obrazów, których wektory wejściowe są przechowywane w pamięci.

```powershell
dotnet run --project TrafficSign-Trainer -- "data" "models/signs.json" --epochs 30 --learning-rate 0.01 --hidden-size 64 --seed 42
```

To ustawienia początkowe, nie obietnica skuteczności. Opcje: `--epochs` 1–10000,
`--learning-rate` >0 i ≤10 (kropka dziesiętna), `--hidden-size` 1–2048,
`--seed` liczba całkowita i opcjonalne `--split-manifest`. Nieznane opcje,
powtórzenia, błędne wartości i istniejące pliki wynikowe są odrzucane.
`--help` pokazuje składnię. Sukces daje kod `0`, błąd komunikat na stderr i kod `1`.

Powstają trzy pliki:

- `signs.json`: model v1, preprocessing, kolejność klas, wagi i biasy;
- `signs.report.json`: ustawienia, wybrana epoka, metryki treningu/walidacji,
  macierze pomyłek, baseline najczęstszej klasy i skróty modelu/danych;
- `signs.split.json`: dokładny podział z SHA256 każdego obrazu i metadanymi grup.

Nie są nadpisywane. Wybierz nową nazwę przy kolejnym eksperymencie. Model jest
publikowany dopiero po przygotowaniu i zapisie raportu oraz podziału.

Bez manifestu program sortuje dane porządkiem ordinal, losuje deterministycznie
całe grupy identycznych plików, roboczo 70/15/15. Proporcje dotyczą grup,
więc przy grupach różnej wielkości liczby zdjęć mogą się różnić. Każda klasa
musi występować we wszystkich częściach. Identyczne pliki z różnymi etykietami
są błędem. Bez metadanych serii program wykrywa tylko identyczne kopie;
podobne ujęcia tego samego znaku mogą pozostać nierozpoznane. Raport wskazuje
ten brak, zamiast deklarować pełną ochronę przed przeciekiem danych.

## Oficjalny podział i serie zdjęć

Opcjonalny manifest opisuje wszystkie obrazy dokładnie raz. Ścieżki są względne
wobec katalogu danych, etykieta nadal wynika z katalogu klasy:

```json
{
  "formatVersion": 1,
  "entries": [
    { "path": "stop/001.png", "split": "train", "groupId": "stop-seria-1" },
    { "path": "stop/002.jpg", "split": "train", "groupId": "stop-seria-1" }
  ]
}
```

To fragment ilustrujący format, a nie kompletny zbiór gotowy do treningu.
`split` przyjmuje `train`, `validation`, `test` albo `null`; albo przypisz go
wszystkim obrazom, albo wszystkim pozostaw `null`. `groupId` jest opcjonalne.
Grupa musi zawierać jedną klasę i w całości należeć do jednej części. Podział
oficjalny train/test zostaje zachowany, a walidacja jest wydzielana wyłącznie
z train. Puste/za małe części powodują błąd zamiast kopiowania przykładów.

```powershell
dotnet run --project TrafficSign-Trainer -- "data" "models/signs.json" --split-manifest "official-split.json"
```

## Ocena wybranego modelu

Trening wybiera epokę o najmniejszej stracie walidacyjnej i nie pokazuje wyniku
testowego. Po zakończeniu strojenia oceń zapisany model, bez ponownego uczenia:

```powershell
dotnet run --project TrafficSign-Trainer -- evaluate "models/signs.json" "data" "models/signs.split.json"
```

Polecenie wymaga oryginalnego raportu treningowego obok modelu. Weryfikuje
model, etykiety, skróty danych i podział; zapisuje `signs.test-report.json`
z accuracy, recall per klasa, stratą i macierzą pomyłek. Wiersze macierzy to
klasy rzeczywiste, kolumny — przewidziane. Jeśli dobierasz ustawienia według
wyniku testowego, nie jest on już niezależną oceną.

## Użycie GUI i format modelu

W oknie wybierz kolejno model JSON i obraz, następnie „Rozpoznaj znak”.
Anulowanie wyboru zachowuje poprzedni stan. Błędny model lub obraz daje
komunikat; po poprawnej zmianie pliku stary wynik znika. Odczyt i predykcja
nie blokują okna. GUI używa tej samej biblioteki `Core` co trener.

Model v1 obsługuje wyłącznie `gray32-v1`: orientacja EXIF, przezroczystość
na białym tle, biliniowe przeskalowanie całego kadru do 32×32, szarość
`(0.299R + 0.587G + 0.114B) / 255`, spłaszczenie wierszami. Obliczenia
pośrednie nie zaokrąglają do bajtów. Etykiety i ich kolejność pochodzą z modelu.
Nieznane wersje, niespójne wymiary i niepoprawne parametry są odrzucane.
Stara luźna tablica wag z początkowego szkieletu nie jest obsługiwanym modelem.

## Test techniczny bez pobierania danych

```powershell
.\tools\New-DemoDataset.ps1
dotnet run --project TrafficSign-Trainer -- "artifacts/demo-data" "artifacts/demo/signs.json" --epochs 10 --hidden-size 8
dotnet run --project TrafficSign-Trainer -- evaluate "artifacts/demo/signs.json" "artifacts/demo-data" "artifacts/demo/signs.split.json"
```

Skrypt tworzy 48 własnych wzorów poziomych/pionowych, bez zewnętrznych plików.
Można wczytać wynikowy model i PNG w GUI. To wyłącznie sprawdzenie przepływu;
**wzory nie są znakami drogowymi, a wynik nie mierzy ich rozpoznawania**.
Skrypt także odrzuca istniejący katalog; przy powtórzeniu użyj nowej ścieżki
przez `-OutputDirectory` i nowej nazwy modelu.

Nie dołączono rzeczywistego zbioru znaków ani modelu wytrenowanego na nim.
Przed oceną skuteczności ustal źródło i wersję danych, warunki użycia oraz
metadane serii. `data/`, `models/`, `runs/` i `artifacts/` są ignorowane przez Git.

## Struktura i zależności

- `TrafficSign.Core`: preprocessing, MLP z backprop i plik modelu;
- `TrafficSign-Trainer`: dane, podziały, SGD, raporty i CLI;
- `TrafficSign-Detector`: jeden ekran WinUI;
- `TrafficSign.Tests`: testy matematyki, wejścia, podziału i CLI.

Dekodowanie używa [SkiaSharp (MIT)](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt).
Nie ma frameworka uczenia maszynowego, serwera ani bazy danych.
Manifest podziału wymaga jawnego `formatVersion: 1`; brak wersji jest błędem.
Istniejące modele, wersjonowane manifesty i raporty v1 zachowują zgodność.

Workflow [build.yml](.github/workflows/build.yml) sprawdza locked restore,
build Release x64 i testy na Windows. Jego wykonanie na GitHub wymaga osobnego
potwierdzenia; lokalny build nie dowodzi sukcesu hostowanego runnera.

Aktualny plan, wynik recenzji i status wdrożenia:
[PLAN-IMPLEMENTACJI.md](docs/PLAN-IMPLEMENTACJI.md).
