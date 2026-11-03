# TrafficSign-Si

Program `TrafficSign-Trainer` przyjmuje dokładnie dwa argumenty:

1. Ścieżkę do pliku JSON z wagami jednej warstwy.
2. Ścieżkę do folderu ze zdjęciami tej klasy (przykłady pozytywne).

```powershell
dotnet run --project TrafficSign-Trainer -- "C:\dane\stop-sign.json" "C:\dane\images\stop"
```

Plik wag to tablica 6913 skończonych liczb. Pierwsza liczba to próg `u0`, kolejne 6912 to kanały RGB pikseli `48 × 48` (czerwony, zielony, niebieski, każdy podzielony przez 255). Gdy pliku nie ma, program tworzy małe losowe wagi. Gdy plik jest i ma 6913 liczb, wczytuje je i kontynuuje naukę. Pliki z poprzedniej sieci 32×32 (1025 liczb) mają inny rozmiar, więc ten bieg zaczyna je od nowa.

Każde zdjęcie jest dekodowane przez `System.Drawing`, skalowane do 48×48 i spłaszczane do 6912 liczb koloru. Zdjęcia leżące bezpośrednio w podanym folderze mają cel `1`. Zdjęcia leżące bezpośrednio w folderach sąsiednich (na przykład `images\ustap`) mają cel `0`. Podfoldery nie są przeglądane. Rozpoznawane rozszerzenia to `.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tif`, `.tiff`, `.webp`, `.ppm` i `.pgm`, bez rozróżniania wielkości liter.

Nauka powtarza epoki, aż naciśniesz dowolny klawisz albo Ctrl+C. W każdej epoce są wszystkie zdjęcia pozytywne i tyle samo losowo wybranych zdjęć negatywnych, żeby klasa z tysiącami sąsiadów nie zagłuszyła znaku. Po każdej epoce program wypisuje średni błąd bezwzględny tej zbalansowanej próbki. Współczynnik uczenia wynosi 0.05. Na końcu zapisuje wagi do tego samego pliku JSON.

Wsadowy trening wszystkich klas z podfolderów, 15 wątków, 60 sekund na znak:

```powershell
dotnet run --project TrafficSign-Trainer -c Release -- --batch "C:\Users\alexv\Desktop\images" "C:\Users\alexv\Desktop\images\weights" 60 15
```

Każdy wątek uczy inny znak. Jeśli `weights\<klasa>.json` już ma 6913 wag, wątek kontynuuje ten plik zamiast losować od nowa. Zdjęcia są wczytywane raz. Negatywami znaku są zdjęcia pozostałych klas, a w epoce jest ich tyle samo co pozytywnych.

Neuron liczy `s = u0 + u1*w1 + ... + u6912*w6912` i przepuszcza `s` przez sigmoid. Brak zdjęć pozytywnych, brak zdjęć w folderach sąsiednich, zła długość wag albo nieczytelny obraz kończą program kodem `1` i komunikatem na standardowym wyjściu błędów. Poprawny zapis wag kończy się kodem `0`.

## TrafficSign-Detector

Aplikacja WinUI wczytuje folder z plikami JSON (jeden neuron na plik). Na liście można włączyć albo wyłączyć poszczególne neurony. Po wybraniu obrazu każdy włączony neuron liczy ten sam sigmoid co Trainer, a program pokazuje nazwę pliku z najwyższym wynikiem.
