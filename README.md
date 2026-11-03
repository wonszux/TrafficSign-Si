# TrafficSign-Si

Program `TrafficSign-Trainer` przyjmuje dokładnie dwa argumenty:

1. Ścieżkę do folderu ze zdjęciami.
2. Ścieżkę do pliku JSON z wagami sieci.

```powershell
dotnet run --project TrafficSign-Trainer -- "C:\dane\zdjecia" "C:\dane\wagi.json"
```

Plik wag musi zawierać niepustą tablicę skończonych liczb:

```json
[0.1, -0.2, 0.3]
```

Program zbiera ścieżki do zdjęć bezpośrednio z podanego folderu (bez podfolderów),
rozpoznając rozszerzenia `.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tif`, `.tiff`,
`.webp`, `.ppm` i `.pgm` bez rozróżniania wielkości liter. Nie dekoduje obrazów.
Wczytuje wagi do `NeuralNetwork` i wypisuje liczbę zdjęć oraz wag.

Nieprawidłowe argumenty, brak plików graficznych lub błędne dane powodują wypisanie
komunikatu na standardowe wyjście błędów i zakończenie z kodem `1`.
Poprawne wczytanie kończy się kodem `0`.
