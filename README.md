# EQD2 Viewer

ESAPI-skripti Varian Eclipseen EQD2-jakaumien tarkasteluun ja uudelleensäteilytyksen kumulatiivisen annosjakauman arviointiin.

## Projektin tila
**Alpha / Beta:** Perustoiminnallisuus on valmiina, mutta kattavaa testausta kliinisillä potilailla ei ole vielä tehty riittävästi. Käytä omalla vastuulla ja tarkista laskelmat aina manuaalisesti.

## Arkkitehtuuri ja ominaisuudet
Projekti on rakennettu Clean Architecture -mallilla, joka eristää Varianin ESAPI-riippuvuudet, käyttöliittymän ja liiketoimintalogiikan toisistaan. 

Tämä mahdollistaa sovelluksen joustavan kehittämisen ja testaamisen paikallisesti:
1. Kliininen data voidaan purkaa Eclipsestä lokaaleiksi JSON-tiedostoiksi `FixtureGenerator`-työkalun avulla.
2. Kehitystyötä ja testausta voidaan jatkaa `DevRunner.exe`-työpöytäsovelluksella täysin ilman Eclipse-ympäristöä.

## Vaatimukset
- Eclipse + ESAPI v15.6 tai uudempi
- .NET Framework 4.8

## Kääntäminen ja asennus
1. Avaa `EQD2Viewer.sln` Visual Studiossa.
2. Varmista yläpalkista, että valittuna on **Release** ja **x64**.
3. Valitse **Build -> Build Solution**.
4. Käännöksen jälkeen projektin juureen ilmestyy `BuildOutput`-kansio, josta löytyvät valmiit asennustiedostot:
   - **01_Eclipse_ESAPI_Plugins:** Kopioi täältä löytyvät `.esapi.dll`-tiedostot suoraan sairaalan Eclipsen skriptikansioon. Costura.Fody on pakannut kaikki tarvittavat riippuvuudet näiden tiedostojen sisään.
   - **02_Standalone_Runner:** Sisältää `DevRunner.exe`:n ja testidatan paikallista käyttöä ja kehitystä varten.

## Käyttö

**Kliininen käyttö (Eclipse):**
1. Avaa potilas ja hoitosuunnitelma Eclipsessä.
2. Aja skripti `EQD2Viewer.App.esapi.dll`.

**Paikallinen kehitys ja testaus:**
1. Avaa kansio `02_Standalone_Runner`.
2. Käynnistä `EQD2Viewer.DevRunner.exe`.

## Versiohistoria

| Versio | Päivämäärä | Kuvaus |
|---|---|---|
| **0.9.1-beta** | 2026-04 | Clean Architecture -uudistus. Erillinen DevRunner offline-kehitykseen, keskitetty BuildOutput-kansiointi ja paranneltu riippuvuuksien hallinta. |
| **0.9.0-beta** | 2026-03 | Beta-vaiheen julkaisu. Ominaisuuksien ja laskentalogiikan vakauttamista. |
| **0.3.0-alpha** | 2026-03 | Automaattinen `.esapi.dll`-päätteen lisääminen käännösvaiheessa (Assembly Name -päivitys projektitiedostoon). |
| **0.2.0-alpha** | 2026-03 | Yksikkötestit (107 kpl), ESAPI-stub-kirjasto CI-kääntämiseen, GitHub Actions -pipeline. |
| **0.1.0-alpha** | 2026-03 | Ensimmäinen alpha. CT/annos-näyttö, isodoosit, EQD2-muunnos, summaatio, DVH, rakennekohtainen α/β. |

## Tekijät
Risto Hirvilammi & Juho Ala-Myllymäki, ÖVPH

## Lisenssi
MIT — ks. `LICENSE.txt`
