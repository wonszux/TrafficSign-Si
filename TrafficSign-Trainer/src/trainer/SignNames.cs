namespace TrafficSign_Trainer.src.trainer
{
    public static class SignNames
    {
        private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A-1_nbzp_prawo"] = "A-1 Niebezpieczny zakręt w prawo",
            ["A-2_nbzp_lewo"] = "A-2 Niebezpieczny zakręt w lewo",
            ["A-3_nbzp_prawo_lewo"] = "A-3 Niebezpieczne zakręty, pierwszy w prawo",
            ["A-4_nbzp_lewo_prawo"] = "A-4 Niebezpieczne zakręty, pierwszy w lewo",
            ["A-5_skrzyzowanie_rownorzedne"] = "A-5 Skrzyżowanie dróg",
            ["A-6_skrzyzowanie_z_podp_po_lewej"] = "A-6 Skrzyżowanie z drogą podporządkowaną po lewej",
            ["A-6a_skrzyzowanie_z_podp_po_obu"] = "A-6a Skrzyżowanie z drogą podporządkowaną po obu stronach",
            ["A-6b_skrzyzowanie_z_podp_po_prawej"] = "A-6b Skrzyżowanie z drogą podporządkowaną po prawej",
            ["A-6d_wlot_z_prawej"] = "A-6d Wlot drogi jednokierunkowej z prawej",
            ["A-6e_wlot_z_lewej"] = "A-6e Wlot drogi jednokierunkowej z lewej",
            ["A-7_ustap_pierwszenstwa"] = "A-7 Ustąp pierwszeństwa",
            ["A-8_skrzyzowanie_okrezne"] = "A-8 Skrzyżowanie o ruchu okrężnym",
            ["A-9_przejazd_zapory"] = "A-9 Przejazd kolejowy z zaporami",
            ["A-10_przejazd_kolejowy"] = "A-10 Przejazd kolejowy bez zapór",
            ["A-11_nierowna_droga"] = "A-11 Nierówna droga",
            ["A-11a_prog_zwalniajacy"] = "A-11a Próg zwalniający",
            ["A-12a_zwezenie_dwustronne"] = "A-12a Zwężenie jezdni dwustronne",
            ["A-12b_zwezenie_prawostronne"] = "A-12b Zwężenie jezdni prawostronne",
            ["A-12c_zwezenie_lewostronne"] = "A-12c Zwężenie jezdni lewostronne",
            ["A-14_roboty"] = "A-14 Roboty na drodze",
            ["A-15_sliska_jezdnia"] = "A-15 Śliska jezdnia",
            ["A-16_przejscie"] = "A-16 Przejście dla pieszych",
            ["A-17_dzieci"] = "A-17 Dzieci",
            ["A-18a_gospodarskie"] = "A-18a Zwierzęta gospodarskie",
            ["A-18b_dzikie"] = "A-18b Zwierzęta dzikie",
            ["A-20_dwukierunkowa"] = "A-20 Ruch dwukierunkowy",
            ["A-21_tramwaj"] = "A-21 Tramwaj",
            ["A-22_nbzp_zjazd"] = "A-22 Niebezpieczny zjazd",
            ["A-23_stromy_podjazd"] = "A-23 Stromy podjazd",
            ["A-24_rowerzysci"] = "A-24 Rowerzyści",
            ["A-28_sypki_zwir"] = "A-28 Sypki żwir",
            ["A-29_sygnaly_swietlne"] = "A-29 Sygnały świetlne",
            ["A-30_inne_nbzp"] = "A-30 Inne niebezpieczeństwo",
            ["A-31_nbzp_pobocze"] = "A-31 Niebezpieczne pobocze",
            ["A-32_oszronienie_jezdni"] = "A-32 Oszronienie jezdni",
            ["A-33_zator"] = "A-33 Zator drogowy",
            ["B-1_zakaz_ruchu"] = "B-1 Zakaz ruchu w obu kierunkach",
            ["B-2_zakaz_wjazdu"] = "B-2 Zakaz wjazdu",
            ["B-3a_zakaz_autobusow"] = "B-3a Zakaz wjazdu autobusów",
            ["B-5_zakaz_ciezarowych"] = "B-5 Zakaz wjazdu samochodów ciężarowych",
            ["B-6_zakaz_ciagnikow"] = "B-6 Zakaz wjazdu ciągników rolniczych",
            ["B-9_zakaz_rowerow"] = "B-9 Zakaz wjazdu rowerów",
            ["B-16_zakaz_wjazdu_wys"] = "B-16 Zakaz wjazdu pojazdów wyższych",
            ["B-18_zakaz_wjazdu_masa"] = "B-18 Zakaz wjazdu pojazdów o większej masie",
            ["B-20_stop"] = "B-20 Stop",
            ["B-21_zakaz_w_lewo"] = "B-21 Zakaz skręcania w lewo",
            ["B-22_zakaz_w_prawo"] = "B-22 Zakaz skręcania w prawo",
            ["B-23_zakaz_zawracania"] = "B-23 Zakaz zawracania",
            ["B-25_zakaz_wyprzedzania"] = "B-25 Zakaz wyprzedzania",
            ["B-26_zakaz_wyprzedzania_ciezarowe"] = "B-26 Zakaz wyprzedzania przez samochody ciężarowe",
            ["B-33_ogr_predkosci"] = "B-33 Ograniczenie prędkości",
            ["B-34_koniec_ogr_predkosci"] = "B-34 Koniec ograniczenia prędkości",
            ["B-35_zakaz_postoju"] = "B-35 Zakaz postoju",
            ["B-36_zakaz_zatrzymywania"] = "B-36 Zakaz zatrzymywania się",
            ["B-39_strefa_postoju"] = "B-39 Strefa ograniczonego postoju",
            ["B-41_zakaz_pieszy"] = "B-41 Zakaz ruchu pieszych",
            ["B-42_koniec_zakazow"] = "B-42 Koniec zakazów",
            ["B-43_strefa_ogr_predkosci"] = "B-43 Strefa ograniczonej prędkości",
            ["B-44_koniec_strefy_ogr_predkosci"] = "B-44 Koniec strefy ograniczonej prędkości",
            ["C-2_nakaz_w_prawo_za"] = "C-2 Nakaz jazdy w prawo za znakiem",
            ["C-4_nakaz_w_lewo_za"] = "C-4 Nakaz jazdy w lewo za znakiem",
            ["C-5_nakaz_prosto"] = "C-5 Nakaz jazdy prosto",
            ["C-8_nakaz_prawo_lub_lewo"] = "C-8 Nakaz jazdy w prawo lub w lewo",
            ["C-9_nakaz_z_prawej"] = "C-9 Nakaz jazdy z prawej strony znaku",
            ["C-10_nakaz_z_lewej"] = "C-10 Nakaz jazdy z lewej strony znaku",
            ["C-11_nakaz_z_prawej_lub_lewej"] = "C-11 Nakaz jazdy z prawej lub z lewej strony znaku",
            ["C-12_ruch_okrezny"] = "C-12 Ruch okrężny",
            ["D-1_droga_z_pierwszenstwem"] = "D-1 Droga z pierwszeństwem",
            ["D-2_koniec_drogi_z_pierwszenstwem"] = "D-2 Koniec drogi z pierwszeństwem",
            ["D-3_droga_jednokier"] = "D-3 Droga jednokierunkowa",
            ["D-4a_droga_bez_przejazdu"] = "D-4a Droga bez przejazdu",
            ["D-5_pierwszenstwo_na_zwezeniu"] = "D-5 Pierwszeństwo na zwężonym odcinku",
            ["D-6_przejscie_pieszy"] = "D-6 Przejście dla pieszych",
            ["D-6b_przejscie_pieszy_i_rowerzysta"] = "D-6b Przejście dla pieszych i przejazd dla rowerzystów",
            ["D-18_parking"] = "D-18 Parking",
            ["D-40_strefa_zamieszkania"] = "D-40 Strefa zamieszkania",
            ["D-41_koniec_strefy_zamieszkania"] = "D-41 Koniec strefy zamieszkania",
            ["D-42_zabudowany"] = "D-42 Obszar zabudowany",
            ["D-43_koniec_zabudowanego"] = "D-43 Koniec obszaru zabudowanego",
            ["D-51_fotoradar"] = "D-51 Fotoradar"
        };

        public static string Display(string className)
        {
            if (Names.TryGetValue(className, out string? display))
            {
                return display;
            }

            return className.Replace('_', ' ');
        }
    }
}
