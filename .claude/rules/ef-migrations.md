# EF Core migration rules

- Any change to `CedarClerk.Server/Data/Entities.cs` requires an immediate migration:
  ```
  dotnet ef migrations add <Name> --project CedarClerk.Server
  ```
  `Database.Migrate()` runs automatically on startup (`Program.cs`), so a schema drift between `Entities.cs` and the latest migration breaks the app on next launch/deploy, not at build time.
- **Column renames**: EF's default diff generates Drop+Add for a renamed property, which **loses data** on an existing SQLite table. Hand-edit the generated migration to use `RenameColumn` instead. (Lesson from an earlier session: renaming a field without a matching migration produced `SQLite Error 'no such column'` on every authorized request, because ASP.NET Identity's security-stamp validation touches `AspNetUsers` on every request.)
- Migrations live in `CedarClerk.Server/Migrations/`, named `{yyyyMMddHHmmss}_{PascalCaseDescription}.cs` — standard `dotnet ef` output, each paired with a `.Designer.cs`, plus one shared `CedarDbContextModelSnapshot.cs`.
- **`SchemaDriftGuardTests` enforces the rule above** (added 27.07.2026). It calls EF 8's `Database.HasPendingModelChanges()` and fails `dotnet test` when `Entities.cs` has moved without a migration — verified to actually go red, not just to exist. Trust it instead of remembering; if it fails, the fix is the `migrations add` command in its assertion message.
- Run `dotnet test` before any deploy that touches `CedarClerk.Core` or `Entities.cs` — see `renderers.md` for why.

## Collapsing the migration chain

The chain is periodically collapsed to a single `InitialCreate` (11.07.2026, dev-only; **27.07.2026, dev + production**). Prod's `__EFMigrationsHistory` currently holds exactly one row: `20260727074652_InitialCreate` / `8.0.29`.

This is a destructive edit of the production database. The 27.07.2026 run is the reference procedure:

1. **Verify equivalence before touching anything.** Generate the new `InitialCreate`, apply it to a scratch DB, and compare against prod *by column set and index set* — not by raw `.schema` text. Raw text always differs harmlessly: prod's tables grew via `ALTER TABLE ADD COLUMN`, which appends columns and requires a `DEFAULT`, while a fresh `CREATE TABLE` uses model order with no defaults. What must match is names/types/nullability and the indexes. (27.07.2026: 27/27 tables, all column sets identical, 40/40 indexes identical.)
2. Back up `cedar.db` via `sqlite3 .backup` and pull a copy off the Pi.
3. **Stop the service**, then `DELETE FROM __EFMigrationsHistory` and insert the single new row.
4. **Deploy the new binaries before starting the service again.** Order is the whole safety argument: a service started with the *old* binaries after the history edit sees 21 unapplied migrations and tries to `CREATE TABLE` over live tables. Stop → edit → deploy → start.
5. Verify afterwards: history holds one row, row counts unchanged, `PRAGMA integrity_check` ok, and the log shows **zero** `CREATE TABLE`/`Applying migration` lines.

Rollback is copying the backup over `cedar.db` and deploying the previous commit, whose migration folder still matches the old history.

**Drift found and fixed on 27.07.2026**: prod had applied `AddDraftTranslationSourceSnapshot` and `AddBlogStatSnapshot`, but both files were missing from the repo while their changes survived in the model snapshot — so the repo could no longer build the schema from scratch, though prod itself was fine. The collapse absorbed them. This is exactly what step 1's column-set comparison is for.
