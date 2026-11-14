# Podsumowanie eksperymentów treningowych

Data: 14 listopada 2026 r.

## Dane i ustawienia

Źródło: `C:\Users\fabia\Desktop\znaki_drogowe\test`. Pomimo nazwy katalogu
jego zawartość podzielono na train/validation/test i wykorzystano do treningu.
Wszystkie próby: **30 epok, 64 neurony ukryte, learning rate 0,01**,
preprocessing `gray32-v1`. Najlepszą epokę wybierano po najniższej stracie
walidacyjnej; test nie uczestniczył w wyborze modelu.

| Wariant | Klasy | Zdjęcia | Train | Validation | Test |
|---|---:|---:|---:|---:|---:|
| Pełny zbiór | 92 | 4298 | 3000 | 649 | 649 |
| Minimum 50 zdjęć na klasę | 26 | 3126 | 2186 | 470 | 470 |

Wariant 26 klas zachował przypisania zdjęć z pierwszego manifestu.
Oryginałów nie usuwano. Dokładny eksport części znajduje się lokalnie na pulpicie
w `znaki_drogowe_26_klas_podzial/{train,validation,test}`.

## Ograniczenie liczby klas — seed 42

| Model i oceniane dane | Accuracy test | Średni recall klas |
|---|---:|---:|
| Model 92 klas, cały test: 649 zdjęć | 71,65% | 52,70% |
| Model 92 klas, tylko zachowane 26 klas: 470 zdjęć | **79,36%** | **73,12%** |
| Model 26 klas, te same 470 zdjęć | 77,66% | 69,37% |

Samo odrzucenie rzadkich klas **nie poprawiło wyniku na tych samych zdjęciach**:
accuracy spadło o 1,70 punktu procentowego. Wzrost względem 71,65% wynika
z oceny łatwiejszego podzbioru. W pełnym zbiorze klasy z najwyżej 10 zdjęciami
treningowymi osiągnęły 32,61% accuracy, a te z ponad 30 — 78,72%.
To związek z liczebnością, nie dowód, że sama liczba zdjęć wyjaśnia wszystkie błędy.

## Porównanie seedów — 26 klas

Manifest był identyczny we wszystkich próbach: zmieniano inicjalizację wag
i tasowanie treningu, zachowując podział danych. Wykorzystano wcześniejszy
ukończony trening seeda 42 i wykonano nowe treningi dla 123 oraz 2026.

| Seed | Najlepsza epoka | Validation loss ↓ | Validation accuracy |
|---|---:|---:|---:|
| **42** | 23 | **0,803991** | 79,79% |
| 123 | 20 | 0,806561 | 80,21% |
| 2026 | 29 | 0,821292 | **81,28%** |

Według ustalonego kryterium wygrywa **seed 42**. Jego końcowe wyniki:
train accuracy **96,66%**, test accuracy **77,66%**, test loss **0,900017**,
średni recall klas testowych **69,37%**. Wyższa accuracy seeda 2026 nie oznacza
niższej straty, która uwzględnia również przypisane prawdopodobieństwa.

## Wnioski i ograniczenia

- Rozpiętość accuracy walidacyjnej między seedami wyniosła 1,49 punktu
  procentowego. W tych trzech próbach zmiana seeda nie dała wyraźnego przełomu.
- Duża różnica train/test i dalszy spadek straty treningowej przy stagnacji
  walidacji wskazują na przeuczenie. Samo wydłużanie treningu nie ma tu mocnego uzasadnienia.
- Kolejny kierunek: uzupełnić rzadkie klasy i obejrzeć pomyłki oraz kadrowanie.
  Model 26 klas ma ograniczony zakres rozpoznawanych znaków.
- To jeden ustalony podział plików; nie sprawdzano kopii ani powiązanych serii.
  W pełnym zbiorze 38 klas miało najwyżej dwa zdjęcia testowe, więc ich wyniki
  są szczególnie niepewne. Test był już analizowany; dalsze strojenie wymaga
  ostrożności w interpretacji jego niezależności.

Raporty, modele i logi są lokalnie w ignorowanym przez Git `artifacts/`:
`trainer-real-20261114-122029`, `trainer-filtered-min50-20261114-122835`
oraz `seed-comparison-20261114-123626`. Ten dokument utrwala ich podsumowanie w repo.
