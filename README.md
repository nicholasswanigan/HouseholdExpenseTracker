# Household Expense Tracker

Create a household, invite roommates or family, log shared expenses, and see who owes whom. Debts are simplified so the group settles up with as few payments as possible.

## Features

- Sign up / log in (ASP.NET Core Identity + JWT), create a household or join one with an invite code
- Add expenses with a payer, amount, category, and split **evenly**, **by percentage**, or by **exact amounts** (splits always add up to the cent)
- Balance dashboard: each person's net balance + suggested payments
- Record settlements when someone pays someone back
- Expense history filtered by month, category, or person
- Users only ever see households they belong to (others return 404)

## Solution layout

| Project | Purpose |
|---|---|
| `src/HouseholdExpenses.Core` | Pure C#, **no DB/web dependencies**: `DebtSimplifier`, `BalanceCalculator`, `SplitCalculator`, shared DTOs/enums |
| `src/HouseholdExpenses.Api` | ASP.NET Core Web API: EF Core + SQLite, Identity + JWT, controllers, Swagger, global error handling |
| `src/HouseholdExpenses.Web` | Blazor WebAssembly front end (talks to the API over HTTP with the JWT) |
| `tests/HouseholdExpenses.Tests` | xUnit: unit tests for the Core logic + integration tests that boot the real API against a temp SQLite file |

## Running it

Requires the .NET 8 SDK.

```bash
# terminal 1 - API on http://localhost:5080 (Swagger UI at /swagger)
dotnet run --project src/HouseholdExpenses.Api --launch-profile http

# terminal 2 - Blazor UI on http://localhost:5173
dotnet run --project src/HouseholdExpenses.Web --launch-profile http
```

Open http://localhost:5173, sign up, create a household, and share the invite code with a second account (use a private window) to try it out.

The SQLite database (`household.db`) is created and migrated automatically on API startup.

```bash
dotnet test        # runs everything
```

## Configuration

| Setting | Where | Notes |
|---|---|---|
| `Jwt:Key` | `appsettings.Development.json` (dev only) | **Must be overridden in production** (>= 32 chars). Use `dotnet user-secrets` or the `Jwt__Key` env var |
| `ConnectionStrings:Default` | `appsettings.json` | defaults to `Data Source=household.db` |
| `Cors:AllowedOrigins` | `appsettings*.json` | the origin(s) the Blazor app is served from |
| `ApiBaseUrl` | `src/HouseholdExpenses.Web/wwwroot/appsettings.json` | where the front end finds the API |

## How the debt simplification works

1. **Net balance** for each member = (everything they paid) - (their share of every expense) + (settlements they paid) - (settlements they received). Positive = owed money; negative = owes money. Balances always sum to zero.
2. **Greedy matching** (`DebtSimplifier`): repeatedly pair the largest debtor with the largest creditor and transfer `min(debt, credit)`. Each payment fully clears at least one person, so n people need at most n-1 payments. Example: A owes B $10 and B owes C $10 -> B nets to zero -> suggestion is **A pays C $10**.

Greedy gives a very good result but isn't guaranteed to be the true minimum for every possible group (that is the NP-hard subset-sum-partition problem). For household-sized groups it's the standard, practical choice.

Money is handled as `decimal`, and uneven splits use largest-remainder allocation: $10.00 / 3 -> $3.34 + $3.33 + $3.33.

## Switching from SQLite to PostgreSQL / SQL Server

1. Swap the package: `Microsoft.EntityFrameworkCore.Sqlite` -> `Npgsql.EntityFrameworkCore.PostgreSQL` or `Microsoft.EntityFrameworkCore.SqlServer`.
2. In `Program.cs` replace `UseSqlite(...)` with `UseNpgsql(...)` / `UseSqlServer(...)` and update the connection string.
3. Migrations are provider-specific: delete `src/HouseholdExpenses.Api/Data/Migrations` and run
   `dotnet ef migrations add InitialCreate --project src/HouseholdExpenses.Api --output-dir Data/Migrations`
   (`dotnet tool restore` installs the pinned `dotnet-ef`).
4. Note: SQLite can't sum/order `decimal` in SQL, so balances are computed in memory. That's fine here and keeps working on other providers.

## API overview (see Swagger for full detail)

```
POST   /api/auth/register | /api/auth/login
GET    /api/households                         POST /api/households
POST   /api/households/join                    GET/PUT/DELETE /api/households/{id}
DELETE /api/households/{id}/members/me         (leave; balance must be settled)
GET    /api/households/{id}/expenses?month=yyyy-MM&category=Rent&userId=...
POST   /api/households/{id}/expenses           GET/PUT/DELETE .../expenses/{expenseId}
GET    /api/households/{id}/balances           (net balances + suggested payments)
GET    /api/households/{id}/settlements        POST .../settlements   DELETE .../settlements/{id}
```

## Design notes / known limits

- Any household member can edit or delete any expense or settlement (household-level trust). Only the owner can rename/delete the household.
- A member can't leave while their balance is non-zero, so the ledger never references someone who is gone with unsettled debt.
- Single currency; the symbol is set in `Fmt.CurrencySymbol` in the Web project.
- JWTs are kept in `localStorage` (simple for a SPA, but exposed to XSS); there are no refresh tokens, so sessions last `Jwt:ExpiryMinutes` (8h default).

## Ideas for next steps

Receipts/attachments, recurring expenses (rent), multi-currency, email invites, activity feed, refresh tokens, pagination on the history endpoint.
