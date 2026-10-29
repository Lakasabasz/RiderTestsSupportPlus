# RiderTestsSupportPlus — założenia projektowe

## Cel

Rozszerzenie okna **Unit Tests** w Riderze o funkcje, których brakuje w wersji wbudowanej:

1. **Zapis i odczyt sesji testowych**, włącznie z testami parametryzowanymi w postaci
   `TestClass(A).TestName(X)`, z **poprawnym rozwiązywaniem** ich po wczytaniu
   (domyślny mechanizm Ridera tego nie robi).
2. **Import sesji z pliku `.runsettings`** w formacie obsługiwanym przez `dotnet vstest`.

Wszystko, co okno oferuje dziś, ma działać bez zmian.

## Kluczowe decyzje

### Rozszerzamy wbudowane okno, nie piszemy własnego
Za oknem Unit Tests stoją runnery (NUnit/xUnit/MSTest), debugowanie testów, dotCover,
Continuous Testing, nawigacja do kodu i integracja z buildem. Odtworzenie tego to
wielomiesięczny projekt, który psułby się przy każdej aktualizacji Ridera.

Dlatego:
- zostaje wbudowane okno, a wtyczka dokłada do niego **akcje** (toolbar i menu kontekstowe):
  *Save Session…*, *Load Session…*, *Import from .runsettings…*,
- logika działa na sesjach i elementach testów przez API **UnitTestFramework** ReSharpera.

### Semantykę filtrów vstest przejmujemy od vstest, nie piszemy własnego parsera
Plik `.runsettings` nie zawiera listy testów, tylko **filtr** (`<RunConfiguration><TestCaseFilter>`).
Zamiast parsować i interpretować wyrażenia (`&`, `|`, `!`, `~`, `Category`, `Traits`…) samodzielnie:
- używamy **`Microsoft.TestPlatform.TranslationLayer`** (programowe API do `vstest.console`),
- `DiscoverTests(assemblies, runsettings)` zwraca `TestCase` z `FullyQualifiedName` / `DisplayName`,
- wynik mapujemy na elementy Ridera.

Dzięki temu dopasowanie jest dokładnie takie jak w `dotnet vstest`.

### Wspólny rdzeń: rozwiązywanie testów po nazwie
Obie funkcje sprowadzają się do tego samego problemu: **mając nazwę testu (także z parametrami),
znaleźć odpowiadający mu element w modelu testów Ridera**. To jest serce wtyczki i od niego zaczynamy.

## Architektura

| Warstwa | Język | Odpowiedzialność |
|---|---|---|
| Frontend (IntelliJ Platform) | Kotlin | akcje w oknie Unit Tests, dialogi wyboru plików, prezentacja błędów/raportów |
| Protokół RD | Kotlin DSL → kod generowany | polecenia frontend → backend (zapisz/wczytaj/importuj) i wyniki |
| Backend (ReSharper) | C# | serializacja sesji, rozwiązywanie elementów, integracja z TranslationLayer, budowanie sesji |

Większość logiki leży w backendzie. Frontend jest cienki.

### Format zapisu sesji (wstępnie)
Własny plik (JSON lub XML). Dla każdego testu:
- projekt i target framework,
- pełna nazwa kwalifikowana z parametrami,
- nazwa wyświetlana,
- łańcuch rodziców (`Namespace → TestClass(A) → TestName(X)`).

### Algorytm rozwiązywania (wstępnie)
1. Dopasuj elementy, które już są w modelu testów.
2. Brakujące: wymuś build i eksplorację metadanych projektu, ponów dopasowanie.
3. Nadal brakujące: dodaj do sesji najbliższego znalezionego rodzica (np. `TestClass(A)`)
   i zawęź go do zapisanych dzieci.
4. Czego nie da się rozwiązać, raportujemy użytkownikowi (nie gubimy po cichu).

## Hipotezy do weryfikacji

- **Przyczyna błędu wbudowanego mechanizmu:** elementy parametryzowane powstają dynamicznie
  (po eksploracji zbudowanego assembly lub po uruchomieniu), więc przy wczytywaniu sesji
  po ID jeszcze nie istnieją. Do sprawdzenia w dotPeek (`JetBrains.ReSharper.UnitTestFramework*.dll`).
- API UnitTestFramework pozwala z wtyczki: tworzyć sesje, wyszukiwać elementy, wymusić eksplorację.
- Grupy akcji okna Unit Tests są dostępne do rozszerzenia (ID do ustalenia przez UI Inspector
  w trybie `idea.is.internal=true`).

## Poza zakresem

- Własne okno testów lub zamiennik wbudowanego.
- Zmiany we wbudowanym grupowaniu, sortowaniu i renderowaniu drzewa.
- Wtyczka dla ReSharpera w Visual Studio (szablon to umożliwia, ale nie jest celem).

## Otwarte pytania

- **Framework testowy:** NUnit (`[TestFixture(args)]` + `[TestCase]`)? Inne?
  Mechanika elementów różni się między frameworkami, więc na start celujemy w jeden.
- **Zawartość `.runsettings`:** czy to `TestCaseFilter`? Czy wchodzą w grę pliki `.playlist`
  z Visual Studio (które faktycznie zawierają listę testów)?
- Format pliku sesji: JSON czy XML; czy ma być kompatybilny z jakimś istniejącym formatem.

## Środowisko i wersje

| Składnik | Wersja | Uwagi |
|---|---|---|
| Rider (cel) | 2026.2.2 | `ProductVersion` w `gradle.properties` |
| ReSharper SDK | 2026.2.2 | `SdkVersion` w `src/dotnet/Plugin.props` — **trzymać w parze z `ProductVersion`** |
| rd-gen | 2026.2.5 | `gradle/libs.versions.toml` |
| Kotlin | 2.3.21 | 2.1.x nie obsługuje JVM 25 |
| Gradle | 9.7.1 | wrapper |
| IntelliJ Platform Gradle Plugin | 2.19.0 | |
| Java | 25 | `rider-model.jar` z Ridera 2026.2 jest skompilowany pod Javę 25 |

IDE przewodnie: **IntelliJ IDEA** (Gradle, `runIde`, frontend). Kod C# edytujemy w **Riderze**.

## Ustalenia z implementacji (2026-09-23)

Sprawdzone w zdekompilowanym SDK 2026.2.2 i na przykładowym projekcie NUnit 4.

- **Hipoteza o przyczynie błędu: niepotwierdzona (błędu nie odtworzyliśmy).** Sesja wbudowana jest reaktywna:
  `UnitTestSession` subskrybuje `UT.Events.Elements.Created` i dołącza elementy pasujące do kryterium, także te
  utworzone po wczytaniu. Są dwie możliwe przyczyny:
  - elementy parametryzowane nie istnieją, dopóki nie przejdzie eksploracja lub przebieg,
    a sesja z samymi nieistniejącymi ID nie ma czego uruchomić;
  - kryterium wymaga zgodności **pełnego** `UnitTestElementId` (GUID projektu, TFM, provider, `TestId`, salt).
  
  Przy okazji: `TestId(UnitTestElementId)` przy eksporcie gubi `Salt`, choć NUnit go nie używa,
  a `TestIdConverter` dzieli zapis po `::`. Wtyczka obsługuje obie przyczyny. Dopasowuje po samym `TestId`
  (projekt i TFM tylko zawężają wynik), wymusza `RescanAll` i w ostateczności cofa się do rodzica.
- **`TestId` w NUnit to dokładnie FQN z adaptera vstest:** `Ns.TestClass("A").TestName(1)`.
  Test: `fixtureId + "." + metoda`, wiersz: `fixtureId + "." + nazwa z argumentami`, fixture: `ns + displayName`.
- **Frontend to proxy akcji backendu.** Akcje okna Unit Tests w Riderze to proxy (`RiderUnitTestAnActionBase(backendActionId)`)
  do akcji backendu, które dostają sesję z data contextu (`UnitTestDataConstants.Session.IN_CONTEXT`).
  Dialogi plików (`ICommonFileDialogs`) i `MessageBox` backendu Rider sam przekazuje do frontendu.
  Mały model RD (`RiderTestsSupportPlusModel`: `saveSession`, `loadSession`, `importRunSettings`) wystawia
  te same przepływy bez dialogów. Służy testom integracyjnym, a akcje wołają ten sam kod backendu.
- **TranslationLayer odrzucony, zamiast niego `dotnet vstest --ListFullyQualifiedTests`.** `DiscoverTests` przez
  TranslationLayer zwraca wszystkie testy i ignoruje `TestCaseFilter`, zarówno z runsettings, jak i z
  `TestPlatformOptions`. vstest stosuje filtr dopiero w ścieżce listowania i uruchamiania konsoli.
  `dotnet vstest <dll> --ListFullyQualifiedTests --ListTestsTargetPath:<plik> --Settings:<runsettings>`
  filtruje silnikiem vstest po właściwościach i traitach `TestCase`. Ścieżkę do `dotnet` bierzemy z toolsetu solucji.
- **Listowanie ≠ przebieg dla `Category` w NUnit.** Podczas przebiegu adapter NUnit sam filtruje i traktuje
  `Category` jako alias `TestCategory`. Silnik vstest przy listowaniu zna tylko `TestCategory`. Przykład:
  `Category!=Slow` wylistował 8 testów, a przebieg wykonał 6. Dla projektów NUnit wtyczka zamienia w kopii
  runsettings samą nazwę właściwości `Category` na `TestCategory`, a wyrażenie dalej ocenia vstest.
  Po tej zmianie listowanie i przebieg są zgodne (6/6, a na przypadku brzegowym 7/7). Inne aliasy specyficzne
  dla adapterów (np. `Priority` w NUnit) nie są obsłużone.
- **`<NUnit><Where>` (tak wyglądają realne pliki, np. `affected-*.runsettings` w BuddyTests).** Listowanie vstest
  go ignoruje i zwraca wszystkie testy (w BuddyTests 31 748 zamiast 80). `Where` stosuje adapter NUnit dopiero
  w trakcie przebiegu. Wtyczka zawiera więc helper `src/tools/RiderTestsSupportPlus.NUnitLister`.
  Uruchamia on `Explore` silnika NUnit z katalogu builda testów (`dotnet exec` z `runtimeconfig.json` i `deps.json`
  testów, tak jak testhost). Tylko eksploracja: nic się nie wykonuje i nic nie jest zapisywane obok testów.
  Gdy są oba filtry, adapter stosuje `TestCaseFilter` i ignoruje `Where`, więc wtyczka robi tak samo.
  Zgodność z prawdziwym przebiegiem sprawdzona na próbce. Na BuddyTests: 80 testów w ~11 s.
- **Provider VSTest ma inne `TestId` (ustalone 2026-10-27, poprawione 2026-10-29, Rider 2026.2).** Dotyczy wyłączonego
  wbudowanego providera NUnit (`UnitTestingSettings.DisabledProviders` → `NUnit3x`) i adaptera NUnit zdjętego z listy
  ignorowanych VSTest. To obejście RIDER-131305: test parametryzowany pod parametryzowanym fixture'em z jedną metodą
  odpala wszystkie case'y metody. Id buduje `VsTestTestCaseMapping.BuildHierarchy`, które przechodzi FQN adaptera:
  - klasa to `executor://nunit3testexecutor/#Ns.C`, fixture parametryzowany `#Ns.C("A")`, metoda `#…C.M`;
  - liść `::<FQN>` powstaje **tylko**, gdy po ostatniej rozpoznanej metodzie zostaje tekst (argumenty case'a);
  - test bez argumentów jest więc samym elementem metody, `#Ns.C.M`, i nie ma formy z `::`;
  - gdy typu nie ma w cache (projekt niezbudowany), całe FQN trafia do jednego liścia `::`.

  URI jest małymi literami, bo normalizuje je `Uri`. `TestElementResolver` próbuje wszystkich form tego samego id
  (FQN, `::`, `#`), gdy podana nie trafia (`VsTestIds.Forms`). Oczekiwane id brakującego testu w kryterium sesji
  dostaje obie formy VSTest, bo przed odkryciem testu nie wiadomo, którą będzie miał. Wcześniej rozpoznawane było
  tylko `::`, więc testy bez argumentów (u nas `ParamFixture(..).Plain`, `Plain.Simple`) były zastępowane całą klasą.
- **Lista ignorowanych adapterów VSTest (`VsTestProviderSettings.ExecutorExcludeList`) działa dwojako.**
  - Wartości (nazwy plików DLL) decydują, czy projekt ma adapter, którego VSTest może użyć. Wpis z pustą wartością
    wyłącza wszystkie adaptery (`CalculateHasTestAdapter`).
  - Klucze (URI executorów) trafiają do runnera, który pomija te executory (`ExtensionsManager`).

  Zdjęcie wpisu w warstwie nadrzędnej wymaga `EntryIndexRemoved` **oraz** atrapy `EntryIndexedValue` w tej samej
  warstwie (dokumentacja `StoreToStorageTranslation`): bez wartości lookup nie znajdzie tej warstwy i usunięcie
  nie działa. Tak wygląda `SampleTestsVsTest.sln.DotSettings`.
- **Test na konfiguracji VSTest (`VsTestProviderTest`) jest wyłączony.** Ustawienia działają, co widać w
  `test_discovery_info`, ale headless IDE testów nie uruchamia discovery VSTest i repozytorium zostaje puste, nawet po
  rescanie. Fix sprawdzony na prawdziwej solucji.
- **Odpowiedzi na otwarte pytania (przyjęte na start):** NUnit 3/4, `.runsettings` = `TestCaseFilter` lub `<NUnit><Where>`
  (bez `.playlist`), format sesji = JSON (Newtonsoft jest w Riderze), rozszerzenie `.rtsession`.

### Co jest zaimplementowane
- `Resolution/`: `TestIdPath` (podział ID z uwzględnieniem nawiasów i literałów, kandydaci na rodzica
  do poziomu klasy), `TestElementResolver` (dopasowanie po `TestId` i zawężenie projektem/TFM),
  `TestResolutionResult.CreateCriterion()` (kryterium sesji).
- `Sessions/`: format `.rtsession`, zapis liści sesji z łańcuchem rodziców, `SessionOpener`:
  rozwiązanie, a gdy czegoś brakuje, `RescanAll` i ponowienie, potem fallback na rodzica i raport.
- `RunSettings/`: `VsTestListing` (proces `dotnet vstest`) i `RunSettingsImporter` (projekty z testami
  i zbudowanym wyjściem, per TFM).
- Akcje backendu `RiderTestsSupportPlus.{SaveSession,LoadSession,ImportRunSettings}` oraz proxy
  we frontendzie w menu *Export/Import* sesji (`Rider.UnitTesting.ExportOptions`).

### Ograniczenia
- Build nie jest wymuszany. Import pomija projekty bez wyjścia builda i raportuje je.
- Import i ponowne skanowanie pokazują postęp w zadaniach w tle Ridera i można je anulować (limit 5 min na assembly).
- Import listuje wyjście builda aktywnej konfiguracji. Raport podaje ścieżkę i czas builda, bo nieaktualny build
  daje po cichu nieaktualną listę.
- Testy integracyjne nie klikają samych akcji (dialogi plików), tylko wołają ich rdzeń przez model RD.
  Rejestrację akcji w menu sprawdza osobny test.

### Testy integracyjne
`./gradlew test` uruchamia Ridera bez okna, z backendem i wtyczką (`lib/testFramework.jar`, JUnit 5),
otwiera `src/test/testData/solutions/SampleTests` (NUnit 4), buduje ją i czeka na eksplorację testów.
Sprawdzane są:
- rejestracja akcji w `Rider.UnitTesting.ExportOptions`,
- import `.runsettings`: dokładnie 6 testów, które wykonuje `dotnet vstest`, i ich obecność w drzewie sesji frontendu,
- wczytanie testów parametryzowanych (argumenty z `.`, `,` i `\"`),
- wczytanie przy zmienionym GUID-zie projektu i TFM,
- fallback na rodzica, z rescanem, dla nieistniejącego `Method(99)`,
- zapis i ponowne wczytanie sesji.
- Fallback na rodzica dodaje go z całym poddrzewem (`TestAncestorCriterion`). Oczekiwane ID testu
  zostaje w kryterium, więc test dołączy do sesji sam, gdy Rider go odkryje.
- Zapisywane są liście sesji, więc testy dodane później do klasy zapisanej jako całość nie wejdą do sesji.

## Narzędzia MCP dla agentów (2026-10-29)

Cel: agent podłączony do Ridera (Claude Code, Junie…) sam tworzy, zmienia i usuwa sesje, bez zaśmiecania
okna Unit Tests i z błędami, które mówią, co zrobić dalej.

- **MCP server Ridera, nie `/ide`.** `/ide` w Claude Code łączy się z własnym pluginem JetBrains, z zamkniętym
  zestawem narzędzi. MCP server Ridera (`com.intellij.mcpServer`, w 2026.2 wbudowany) ma dynamiczny extension point
  `mcpServer.mcpToolset`, z którego korzysta sam Rider (dotCover `findTests`, analiza, debugger). Wtyczka rejestruje
  `TestSessionToolset` w `testssupportplus-mcp.xml`, czyli opcjonalnej zależności. Bez MCP Server reszta działa jak dotąd.
- **Frontend tylko deklaruje.** Nazwy, parametry i opisy narzędzi są w Kotlinie. Wywołanie idzie jednym callem RD
  `mcpTool(tool, argumentsJson) → json` do `Mcp/SessionTools.cs`. Kontrakt widziany przez agenta (walidacja, błędy,
  sugestie) jest w całości w backendzie, w jednym miejscu.
- **Koperta odpowiedzi.** Odpowiedź ma pola `status` (`ok`/`partial`/`failed`), `error{code,message}`, `result`,
  `warnings[]` i `suggestions[]`. Sugestia to gotowe wywołanie (narzędzie i argumenty) albo `userAction`, gdy tylko
  użytkownik może coś zrobić (ustawienia, odblokowanie sesji). Częściowy wynik nie jest błędem. `failed` oznacza,
  że nic nie zmieniono. Kody są w `ProblemCodes`, a agenci się na nich opierają, więc nie wolno ich zmieniać.
- **CRUD bez nowych zakładek.** Wszystko jest sprawdzone w zdekompilowanym SDK 2026.2.2:
  - update ustawia `IUnitTestSession.Criterion` (sesja reaguje na zmianę, tak jak przy wbudowanym „Add to session”):
    dodawanie przez `AddElementsMatching`, usuwanie przez `ExcludeElementsMatching(TestAncestorCriterion)`
    razem z potomkami;
  - delete wywołuje `IUnitTestSessionRepository.DestroySession`. Conductor nasłuchuje `SessionDestroyed` i zamyka
    zakładkę, a sesja znika ze stanu przywracanego po starcie;
  - `create` z zajętą nazwą zwraca błąd `SESSION_EXISTS` z sugestią `update` albo `ifExists=replace`.
- **Sesje użytkownika chronione.** Guidy sesji utworzonych przez agenta są w ustawieniach solucji
  (`AgentSessionsSettings`, ten sam kontekst co `UnitTestSessionStore` Ridera), więc przetrwają restart.
  `update` i `delete` na cudzej sesji wymagają `allowUserSession: true`, a na zablokowanej (`IsLocked`) albo
  uruchomionej (`!IsIdle`) dają błąd.
- **Rescan nie blokuje agenta.** `SessionOpener.ResolveAsync` czeka na rescan najwyżej 60 s. Potem zwraca to, co jest
  (`RESCAN_TIMED_OUT`), a oczekiwane id zostają w kryterium, więc testy dołączą do sesji same.
- **Podpowiedzi id** (`SimilarTests`) porównują id w formie frameworka bez wielkości liter, cudzysłowów i spacji
  (`ParamFixture(A)` → `ParamFixture("A")`). Do tego testy o tej samej nazwie w innych klasach.
  Gdy da się poprawić argument `tests`, sugestią jest to samo wywołanie z poprawionymi id.
- **Odkrywanie testów** (`Discovery/DiscoveryInfo`). Najpierw stan faktyczny dla każdego projektu i TFM:
  elementy w repozytorium według providera i executora VSTest, stan builda, obecność `NUnit3.TestAdapter.dll`.
  Potem efektywne ustawienia: `DisabledProviders`, `VsTestProviderSettings.ExecutorExcludeList` (VSTest dopasowuje
  nazwy plików adapterów, czyli wartości, nie URI), `NUnitProviderSettings.TestDiscoveryFromArtifactsMethod`,
  domyślny runsettings Ridera i jego autodetekcja. Wyprowadzone `implications` mówią to wprost, np.
  „NUnit3x wyłączony → id z prefiksem executora, testy pojawiają się dopiero po buildzie”.
- **Build** (`Discovery/BuildOutput`): brak DLL aktywnej konfiguracji (`BUILD_OUTPUT_MISSING`) albo DLL starsza niż
  najnowsze źródło lub plik projektu, również w projektach przez referencje (`BUILD_OUTPUT_STALE`). Do tego build
  w toku (`ISolutionBuilder.RunningRequest`). Sugestia to sekwencja `build_solution_start` → `build_solution_state`
  → ponowienie wywołania. Sama wtyczka nie buduje: jeden mechanizm builda w IDE, a błędy kompilacji widać
  w `build_solution_state`. Nieaktualny build to tylko ostrzeżenie, bo porównanie czasów jest heurystyką: build bez
  zmian nie dotyka DLL, a twardy błąd zapętliłby agenta. Import `.runsettings` w oknie Unit Tests też teraz zgłasza
  nieaktualny build.

## Plan

1. **Prototyp rozwiązywania** (backend): akcja, która dla podanej nazwy
   `TestClass(A).TestName(X)` znajduje element testu i raportuje wynik.
2. Zapis i odczyt sesji na bazie prototypu.
3. Import z `.runsettings` przez TranslationLayer.
4. Akcje w oknie Unit Tests (frontend) i model RD.
