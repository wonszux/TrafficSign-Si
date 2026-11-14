# Podsumowanie refaktoryzacji C#/.NET

Data aktualizacji: 14 listopada 2026 r.

Zrealizowano uproszczenie przygotowania danych i przebiegu treningu. Zachowano
sieć `1024 → tanh → softmax`, SGD, inicjalizację Xavier, seed, wybór najlepszej
epoki według straty walidacyjnej oraz wspólny preprocessing SkiaSharp
`gray32-v1`. Format modelu v1, frameworki docelowe i zależności pozostają bez zmian.

## Dane i manifesty

- Każdy plik jest osobną próbką. Usunięto grupowanie, union-find, obliczanie
  SHA256 obrazów, kontrolę kopii i kompletności metadanych grupowania.
- Zachowano cztery tryby: automatyczny podział bez manifestu, automatyczny
  podział z nieprzypisanym manifestem, pełne przypisania train/validation/test
  oraz train/test z wydzieleniem walidacji wyłącznie z train.
- Zachowano sortowanie ordinal i deterministyczne tasowanie. Dla danych bez
  dawnych grup i kopii przypisania pozostają takie same. Automatyczny podział
  wymaga trzech plików każdej klasy; wydzielenie walidacji — dwóch plików train.
- Nowy manifest v2 zapisuje tylko `path` i `split`. Odczyt wymaga jawnej wersji
  1 lub 2. W v1 dawne pola `sha256` i `groupId` są ignorowane, a ich obecność
  powoduje jeden zbiorczy komunikat. Ręczna migracja nie jest potrzebna.
- Nadal sprawdzane są bezpieczne ścieżki, kompletność manifestu, powtórzone
  ścieżki, poprawność przypisań i obecność każdej klasy we wszystkich częściach.
  Zachowano dekodowanie i walidację JPEG/PNG oraz limit 20 000 obrazów w RAM.
- Manifest potwierdza listę ścieżek, ale nie niezmienność zawartości obrazów.
  Identyczne kopie mogą należeć do różnych części i klas. Rozłączność kopii
  i powiązanych serii musi zapewnić osoba przygotowująca dane.

## Trening, raport i publikacja

- W każdej epoce wspólny przebieg metryk liczy jedynie loss/accuracy dla
  train/validation. Macierze pomyłek i recall powstają dopiero na końcu.
- Po przywróceniu najlepszego modelu obliczane są końcowe metryki train,
  validation i jeden pomiar testu. Test nie uczestniczy w uczeniu ani wyborze epoki.
- Jeden raport v2 zawiera etykiety, opcje, liczebności części według klas,
  najlepszą epokę, historię loss/accuracy oraz końcowe loss, accuracy, recall
  i macierze pomyłek. Usunięto baseline większościowy i metadane środowiska.
- Usunięto fingerprint zbioru, hashowanie modelu i wiązanie ewaluacji
  z tożsamością wcześniejszego eksperymentu. W tym przepływie nie ma hashy.
- Usunięto komendę `evaluate` i osobny `.test-report.json`. Dawne raporty v1
  pozostają archiwalne i nie są wymagane przez nowy przepływ.
- Trening publikuje trzy pliki: model, `.split.json` i `.report.json`.
  Zachowano odmowę nadpisania, pliki tymczasowe, sprzątanie po błędzie
  i publikację modelu jako ostatniego pliku, oznaczającego komplet wyników.
- README opisuje nowe CLI, manifesty, zgodność formatów i odpowiedzialność
  za rozłączność danych. Po wdrożeniu i recenzji usunięto oba dokumenty planów.

## Weryfikacja i recenzja

- Przed zmianami przechodziło **49 testów**. Po zmianach przechodzą **64 testy**,
  obejmujące matematykę i serializację modelu, JPEG/PNG, osiem orientacji EXIF,
  przezroczystość, wszystkie tryby podziału, manifesty v1/v2 i błędne manifesty.
- Nowe regresje sprawdzają akceptację kopii między częściami i klasami,
  ignorowanie dawnych metadanych i zmienionej zawartości obrazu, schemat raportu,
  brak wpływu testu na historię i model oraz odtwarzanie najlepszej epoki,
  także gdy późniejsze epoki mają gorszą stratę walidacyjną.
- Locked restore całego rozwiązania zakończył się powodzeniem. Nie zmieniono
  pakietów ani plików `packages.lock.json`.
- Kompilacje Release i Debug x64 z `-warnaserror` zakończyły się bez błędów
  i ostrzeżeń kompilacji. Kontrola `dotnet format --verify-no-changes
  --no-restore --severity warn` i `git diff --check` przeszła.
- Pełny przepływ CLI na 48 obrazach demonstracyjnych: 5 epok, 4 neurony ukryte,
  learning rate 0,01 i seed 42. W porównaniu z wersją sprzed uproszczenia,
  przy tym samym zapisanym podziale, model JSON był identyczny bajt po bajcie.
  Historia loss/accuracy, najlepsza epoka (5) oraz wszystkie końcowe metryki
  train/validation/test były identyczne. Końcowe loss: train
  0,4229456906876184, validation 0,4303827618099946, test 0,39220737811303064;
  accuracy 1 i recall obu klas 1 we wszystkich częściach. Różnice tych metryk
  przed i po wyniosły zero. Wyniki lokalne: `artifacts/simplification-20261114/`.
- Kod produkcyjny trenera zmniejszył się z 895 do 665 linii (o 230),
  bez dodawania zależności. Testy zmieniono zgodnie z nowymi kontraktami.
- Niezależny subagent zrecenzował implementację po jej zakończeniu i nie znalazł
  błędów wymagających poprawki. Zasugerował mocniejszy test odtwarzania
  najlepszej epoki; dodano go i otrzymano końcową recenzję bez dalszych uwag.
- Zastana lokalna zmiana komentarza XML w `TrafficSign.Core/ModelFile.cs`
  została zachowana bez modyfikacji.

## Zachowane efekty wcześniejszej refaktoryzacji

Iteracja z 9 listopada uporządkowała nazewnictwo pól i indeksów, wydzieliła
obliczanie aktywacji i logitów w sieci oraz oddzieliła dekodowanie obrazu od
skalowania. Pozostają walidacja modelu, uporządkowana publikacja plików,
diagnostyka błędów sprzątania i dokumentacja kontraktów Core. GUI przechowuje
sieć i etykiety, wykonuje odczyt i predykcję asynchronicznie oraz zachowuje stan
po anulowaniu wyboru. `.editorconfig`, analizatory SDK i kontrola formatowania
w CI nadal obowiązują.

## Ograniczenia weryfikacji

- Dane demonstracyjne są wzorami poziomymi/pionowymi, a nie znakami drogowymi.
  Wynik potwierdza zgodność techniczną, nie brak utraty skuteczności na
  rzeczywistych znakach. Nie dostarczono rzeczywistego niezależnego testu.
- Detector uruchomił się jako aplikacja packaged i wyświetlił ekran oraz dialog
  wyboru modelu. Pełny ręczny przepływ GUI (model, JPEG/PNG, predykcja i błędy)
  nie został potwierdzony: narzędzie automatyzacji nie udostępniało okna
  `PickerHost.exe` jako celu, a próba interakcji była odrzucana także po
  odświeżeniu i ponownym wyborze okna. Testy Core i kompilacja GUI przechodzą.
- Formatter zgłasza ogólne ostrzeżenie ładowania obszaru roboczego WinUI,
  kończąc kontrolę z kodem 0. Nie jest to ostrzeżenie kompilacji kodu źródłowego.
- Nie uruchamiano workflow na GitHub; lokalne sprawdzenia nie potwierdzają
  działania hostowanego runnera.
