# Title Claim Intelligence

Title Claim Intelligence is the in-place modernization of the existing Title Claim Tracker. It is a .NET 8 ASP.NET Core Web API with an Angular client, ML.NET triage services, ASP.NET Core Identity, and a script-managed SQL Server database.

The application is a property-dispute intake and workflow tool. It is not a legal decision system and does not file documents with a court.

## Phase 1 complete: secure User/Admin workflows

- Public registration always provisions the `User` role; clients cannot choose a role.
- Optional Admin provisioning uses `UserManager` and `RoleManager` with externally supplied settings.
- Login, logout, current-session restoration, inactive-account rejection, and cookie expiration handling are implemented.
- Cookie-authenticated unsafe requests use ASP.NET Core antiforgery with the `XSRF-TOKEN` cookie and `X-XSRF-TOKEN` header.
- Trusted CORS origins are configuration-driven. Same-origin deployment is preferred; the Angular development proxy avoids browser CORS.
- The API uses server-side `[Authorize]` and Admin role checks. The client route guards are navigation assistance only.
- Regular-user filing queries filter by `SubmittedByUserId` and active rows. Admin reads include active legacy rows whose owner is `NULL`.
- The Angular shell restores `/api/auth/me`, handles 401 expiration, and provides Home, Register, Login, Forbidden, My Workspace, and Admin routes.

No database script was added for Phase 1. The existing 2.0.3 schema already contains the Identity, ownership, soft-delete, history, and audit structures required by this phase.

## Request flow

```text
Angular shell
  ├─ restores CSRF cookie and /api/auth/me
  ├─ sends relative /api requests with credentials and XSRF header
  └─ redirects on expired protected sessions
        ▼
ASP.NET Core API
  ├─ Identity cookie + active-account validation
  ├─ antiforgery and trusted-origin checks
  ├─ controller authorization and DTO validation
  └─ ownership-filtered services/repositories
        ▼
SQL Server 2.0.3
  ├─ AspNetUsers / AspNetRoles
  ├─ LegalFilings with SubmittedByUserId and soft delete
  ├─ ClaimStatusHistory and AuditLog
  └─ triage, model, idempotency, and public-record structures
```

A claimant/property owner is data in a claim and is not necessarily the authenticated submitting account. The server derives account identity, roles, audit actors, and ownership; it does not trust client-supplied values for those fields.

## Authentication endpoints

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/auth/csrf` | Anonymous | Stores the browser XSRF cookie before unsafe requests. |
| `POST` | `/api/auth/register` | Anonymous + XSRF | Creates a User account and starts a session. |
| `POST` | `/api/auth/login` | Anonymous + XSRF | Starts an Identity cookie session. |
| `POST` | `/api/auth/logout` | Authenticated + XSRF | Ends the current session. |
| `GET` | `/api/auth/me` | Authenticated | Returns the active account and server-confirmed roles. |

The auth cookie is HttpOnly and SameSite Lax. Its secure policy follows the request so the local HTTP Angular development proxy can work; HTTPS production traffic still receives a secure cookie. The XSRF cookie is readable by Angular by design and is not an authentication credential.

## Phase 2 core API

The API below implements the secured claim and assisted-intake workflow. It validates mutation DTOs, applies ownership restrictions in server-side queries, uses stable paging, preserves status/audit history, and requires rowversion values for every Admin mutation. The complete Angular experience, audit-history UI, and analytics phases remain open.

| Method | Endpoint | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/filings` | Authenticated | Ownership-filtered paged search. |
| `GET` | `/api/filings/{id}` | Authenticated | Ownership-filtered detail lookup. |
| `POST` | `/api/filings` | Authenticated + XSRF | Creates a manually submitted `New` claim; requires `Idempotency-Key`. |
| `PUT` | `/api/filings/{id}` | Admin + XSRF | Edits a claim using its Base64 rowversion. |
| `PATCH` | `/api/filings/{id}/status` | Admin + XSRF | Applies an allowed status transition using its Base64 rowversion. |
| `DELETE` | `/api/filings/{id}` | Admin + XSRF | Soft deletes a filing; requires `If-Match` with its Base64 rowversion. |
| `GET` | `/api/filings/report` | Admin | Executes the existing authorized reporting procedure. |
| `GET` | `/api/filings/export` | Admin | Exports the current filtered result set with formula-safe CSV fields. |
| `POST` | `/api/chatbot/triage` | Authenticated + XSRF | Persists an assisted-intake attempt and returns an explicit outcome. |
| `POST` | `/api/chatbot/drafts/submit` | Authenticated + XSRF | Explicitly submits an owned draft; requires `Idempotency-Key`. |
| `GET` | `/api/chatbot/review` | Admin | Lists unresolved, unreviewed intake attempts. |
| `POST` | `/api/chatbot/review/{attemptId}/feedback` | Admin + XSRF | Records one reviewed classification correction. |

`Idempotency-Key` values are scoped to the authenticated account and operation, are compared against a server-calculated request hash, and replay the saved result only when the same payload is retried. A different payload or an active matching request returns `409`. Triage attempts use the deployed `AwaitingConfirmation`, `Created`, `Rejected`, and `Failed` states; API outcomes such as `needsInformation`, `needsReview`, and `outOfScope` are mapped without adding unsupported database values.

## Database safety

Use [`Database/DatabaseSetup.md`](Database/DatabaseSetup.md) for the deployed database. Run the complete [`Upgrade_TitleClaimIntelligence.sql`](Database/Upgrade_TitleClaimIntelligence.sql) once in the selected existing database and then run [`Verify_TitleClaimIntelligence.sql`](Database/Verify_TitleClaimIntelligence.sql).

Do not run `Database.Migrate()`, `EnsureCreated()`, `dotnet ef database update`, the retired initializer, or root-level `TableCreation.sql`. The application does not create or alter schema objects at startup. Existing rows, categories, statuses, Identity composite key constraints, nullable historical audit dates, and soft-deleted claims are preserved. Future schema work must be a separate reviewed versioned script; Phase 1 requires none.

## Configuration and startup

From the `TitleClaimTracker` directory:

```powershell
dotnet restore
dotnet build
$env:ConnectionStrings__DefaultConnection = 'Server=localhost\SQLEXPRESS01;Database=TitleClaimTracker;Integrated Security=True;TrustServerCertificate=True;'
dotnet run
```

The launch profile uses `https://localhost:58011` and `http://localhost:58012`. The development Angular proxy targets `https://localhost:58011`:

```powershell
Push-Location .\ClientApp
npm ci
npm start
Pop-Location
```

Set `Identity:BootstrapAdmin:Enabled`, `Identity:BootstrapAdmin:Email`, and `Identity:BootstrapAdmin:Password` through environment variables or a secret store for a one-time admin bootstrap. Disable the flag afterward. Never store the password in Git, SQL, or client code. Configure `Cors:AllowedOrigins` only for explicitly trusted separate frontend origins; do not use `*` with credentials.

## Isolated tests

The test project is `../TitleClaimTracker.Tests/TitleClaimTracker.Tests/TitleClaimTracker.Tests.csproj`. It uses EF Core InMemory and mocks, not the existing SQL Server database:

```powershell
dotnet restore ..\TitleClaimTracker.Tests\TitleClaimTracker.Tests\TitleClaimTracker.Tests.csproj
dotnet test ..\TitleClaimTracker.Tests\TitleClaimTracker.Tests\TitleClaimTracker.Tests.csproj --no-restore
```

Latest isolated validation: 12 tests discovered, 10 passed, 2 browser smoke tests explicitly skipped because they require a separately started application and browser driver, and 0 failed. The backend build passed. The Angular production build has not been rerun in this session.

## Remaining implementation phases

- Add integration coverage against a disposable SQL Server database for concurrent idempotency and rowversion conflicts, then implement the full Angular claim/admin, audit-history, reporting, and export experience.
- Replace the eight-example ML.NET bootstrap baseline with provenance-controlled datasets, leakage-safe splits, evaluation/calibration metrics, and versioned artifacts.
- Add schema-validated provider extraction plus a deterministic fallback and explicit availability/rate-limit behavior.
- Add Tableau Embedding API v3 assets, metric definitions, reconciliation queries, and publishing instructions using approved synthetic/public data only.
- Add optional checkpointed NYC ACRIS ingestion without writing public records into `LegalFilings` or treating document types as dispute labels.

These items remain open until implemented and tested; no accuracy figures, external credentials, workbook URLs, datasets, or deployments are fabricated.
