# Bookstore demo

A small NUnit 4 solution for trying Rider Tests Support Plus and taking screenshots of it:
a bookstore's pricing, ISBN validation, cart and orders, with 52 passing tests.

| Project | What it shows |
|---|---|
| `src/Bookstore.Core` | Code under test |
| `tests/Bookstore.Core.Tests` | Parametrized fixture `PriceCalculatorTests("PL",0.05d)` × 3 countries, `TestCase` arguments with dots, commas, spaces and quotes, categories `Smoke`, `Slow`, `EdgeCase` |
| `tests/Bookstore.Orders.Tests` | Second test project, `TestCaseSource` with `SetName`, fixture parametrized by an enum, category `Integration` |

Open `Bookstore.sln` in Rider with the plugin installed and build it (import lists the build output).

## Scenarios

### Save and load a session
1. In **Unit Tests** create a session with a few parametrized tests, e.g.
   `PriceCalculatorTests("PL",0.05d)` → `GrossPrice(39.99d)` and `IsbnTests` → `InvalidIsbn("\"9780306406157\"")`.
2. Session tab → *Export/Import* → *Save Session (Tests Support Plus)…*
3. Close the session, then *Load Session (Tests Support Plus)…*: the same tests, resolved by name.

Or load a ready one: `sessions/checkout.rtsession` (9 tests from both projects).

### Load a session saved before a refactoring
*Load Session…* → `sessions/outdated.rtsession`. Three of its tests no longer exist:

| Saved test | Resolved to |
|---|---|
| `PriceCalculatorTests("PL",0.05d).GrossPrice(49.99d)` | the method `GrossPrice` of that fixture |
| `PriceCalculatorTests("FR",0.055d).BulkDiscount(3,0.05d)` | the class `PriceCalculatorTests` (there is no `FR` fixture) |
| `IsbnTests.ValidIsbn("978-83-01-00000-1")` | the method `ValidIsbn` |

The plugin rescans the solution first, then falls back to the nearest parent and reports every test it
didn't resolve exactly.

### Import a session from .runsettings
*Export/Import* → *Import Session from .runsettings…* with a file from `runsettings/`:

| File | Selection | Tests |
|---|---|---|
| `smoke.runsettings` | `TestCaseFilter`: `Category=Smoke`, from both projects | 14 |
| `fast.runsettings` | `TestCaseFilter`: `Category!=Slow` | 47 |
| `pricing-poland.runsettings` | `NUnit/Where`: the `PL` fixture and `IsbnTests` | 16 |
| `warehouse.runsettings` | `NUnit/Where`: `Integration` without `Slow` | 4 |

The counts are what `dotnet test --settings <file>` runs. Import runs in the background with progress;
the report says which assemblies the tests were listed from.

## Run the tests without Rider

```
dotnet test Bookstore.sln
dotnet test Bookstore.sln --settings runsettings/smoke.runsettings
```
