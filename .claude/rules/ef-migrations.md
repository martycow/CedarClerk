# EF Core migration rules

- Any change to `CedarClerk.Server/Data/Entities.cs` requires an immediate migration:
  ```
  dotnet ef migrations add <Name> --project CedarClerk.Server
  ```
  `Database.Migrate()` runs automatically on startup (`Program.cs`), so a schema drift between `Entities.cs` and the latest migration breaks the app on next launch/deploy, not at build time.
- **Column renames**: EF's default diff generates Drop+Add for a renamed property, which **loses data** on an existing SQLite table. Hand-edit the generated migration to use `RenameColumn` instead. (Lesson from an earlier session: renaming a field without a matching migration produced `SQLite Error 'no such column'` on every authorized request, because ASP.NET Identity's security-stamp validation touches `AspNetUsers` on every request.)
- Migrations live in `CedarClerk.Server/Migrations/`, named `{yyyyMMddHHmmss}_{PascalCaseDescription}.cs` — standard `dotnet ef` output, each paired with a `.Designer.cs`, plus one shared `CedarDbContextModelSnapshot.cs`.
- Migration history was intentionally collapsed to a single `InitialCreate` on 11.07.2026 (dev DB only). Before any deploy, confirm prod's `__EFMigrationsHistory` table still matches — verified in sync as of 15.07.2026 via direct SSH check (`InitialCreate` + `AddTelegramLinkedAt`, prod data intact: 2 users, 9 drafts, 3 channels at that time).
- Run `dotnet test` before any deploy that touches `CedarClerk.Core` or `Entities.cs` — see `renderers.md` for why.
