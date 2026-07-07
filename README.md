# FluentMigrator.IdempotentExtensions

Idempotent extension methods for [FluentMigrator](https://fluentmigrator.github.io/) migrations — safe to run multiple times on any database.

[![NuGet](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions.svg)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/)
[![NuGet (SqlServer)](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer.svg)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/)
[![CI](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml/badge.svg)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml)

## Packages

| Package | Description |
|---------|-------------|
| `TropinAlexey.FluentMigrator.IdempotentExtensions` | DB-agnostic helpers (SQL Server, PostgreSQL, MySQL, SQLite) |
| `TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer` | SQL Server / Azure SQL specific helpers |

## Installation

```shell
dotnet add package TropinAlexey.FluentMigrator.IdempotentExtensions
```

For SQL Server-specific helpers (e.g. `DropDefaultConstraintIfExists`):

```shell
dotnet add package TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer
```

## Quick Start

```csharp
using FluentMigrator;
using FluentMigrator.IdempotentExtensions;

[Migration(20240101)]
public class CreateUsersTable : Migration
{
    public override void Up()
    {
        // Creates table only if it doesn't exist
        this.CreateTableIfNotExists("users", t => t
            .WithIdColumn()                              // id INT NOT NULL PRIMARY KEY IDENTITY
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("email").AsString(500).Nullable());

        // Adds column only if it doesn't exist
        this.CreateColumnIfNotExists("users", "created_at",
            c => c.AsDateTime().Nullable());

        // Creates index only if it doesn't exist
        this.CreateIndexIfNotExists("users", "email",
            idx => idx.Ascending());

        // Creates unique constraint only if it doesn't exist
        this.CreateUniqueConstraintIfNotExists("users", "uc_users_email", new[] { "email" });

        // Creates audit log table (users_log) if it doesn't exist
        this.CreateLogTableIfNotExists("users");
    }

    public override void Down()
    {
        this.DropTableIfExists("users_log");
        this.DropTableIfExists("users");
    }
}
```

## Use Cases

#### Idempotent CI/CD deploys

Migration runners are re-invoked on retries, rolling restarts, and repeated pipeline runs. Every method here checks existence first, so the same migration can be applied any number of times without ever failing on "table/column/index already exists".

#### Expand-contract schema changes

Add a new column, backfill it in application code, then drop the old one in a later release — using `CreateColumnIfNotExists` and `DeleteColumnIfExists` for each step. If a deploy fails partway through and the migration re-runs, already-applied steps are skipped instead of throwing.

#### Renaming columns across environments at different versions

`RenameColumnIfExists` only renames when the old column name still exists. Useful when dev/staging/prod (or per-tenant databases) aren't all on the same migration checkpoint — environments where a manual fix already renamed the column are left alone.

#### Adding indexes and constraints after the fact

`CreateIndexIfNotExists`, `CreateCompositeIndexIfNotExists`, and `CreateUniqueConstraintIfNotExists` let you introduce a performance index or a new uniqueness rule without checking whether a previous migration, hotfix, or DBA already created it.

#### Multi-tenant / multi-schema databases

Every method accepts `schemaName`, so the same migration class can loop over several tenant schemas (or `dbo` / `public` / a per-tenant schema) in one pass instead of duplicating migration logic per schema.

#### Standardized audit logging

`CreateLogTableIfNotExists` bootstraps a `{table}_log` table (`id`, `timestamp`, `username`, `action`, `record_id`) for any entity table with one call, instead of hand-writing the same audit schema for every table that needs one.

#### Safe rollback / cleanup steps

`DropTableIfExists`, `DropConstraintIfExists`, `DropPrimaryKeyIfExists`, `DropIndexIfExists`, and `DropSchemaIfExists` make `Down()` migrations and manual recovery scripts safe to re-run, since dropping something that isn't there is a no-op instead of an exception.

#### SQL Server: altering a column blocked by an auto-named DEFAULT constraint

SQL Server auto-generates DEFAULT constraint names, so you often can't `DROP CONSTRAINT` by a name you control before an `AlterColumn`. `DropDefaultConstraintIfExists` (SqlServer package) locates and drops it by column name instead.

```csharp
this.DropDefaultConstraintIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```

## API Reference

### Core package (`TropinAlexey.FluentMigrator.IdempotentExtensions`)

#### `WithIdColumn()`

```csharp
ICreateTableColumnOptionOrWithColumnSyntax WithIdColumn(
    this ICreateTableWithColumnSyntax tableWithColumnSyntax)
```

Adds `id INT NOT NULL PRIMARY KEY IDENTITY` to a `Create.Table(...)` chain.

---

#### `CreateTableIfNotExists()`

```csharp
IFluentSyntax? CreateTableIfNotExists(
    this Migration self,
    string tableName,
    Func<ICreateTableWithColumnOrSchemaOrDescriptionSyntax, IFluentSyntax> constructTable,
    string schemaName = "dbo")
```

Creates a table only if it does not already exist. Returns `null` if already exists.

---

#### `DropTableIfExists()`

```csharp
void DropTableIfExists(
    this Migration self,
    string tableName,
    string schemaName = "dbo")
```

Drops a table only if it exists. No-op otherwise.

---

#### `CreateColumnIfNotExists()`

```csharp
IFluentSyntax? CreateColumnIfNotExists(
    this Migration self,
    string tableName,
    string colName,
    Func<IAlterTableColumnAsTypeSyntax, IFluentSyntax> constructCol,
    string schemaName = "dbo")
```

Adds a column to an existing table only if that column does not exist. Returns `null` if the table or column is already present.

---

#### `DeleteColumnIfExists()`

```csharp
void DeleteColumnIfExists(
    this Migration self,
    string tableName,
    string colName,
    string schemaName = "dbo")
```

Drops a column only if it exists. No-op otherwise.

---

#### `RenameColumnIfExists()`

```csharp
void RenameColumnIfExists(
    this Migration self,
    string tableName,
    string oldName,
    string newName,
    string schemaName = "dbo")
```

Renames a column only if the source column exists. No-op if `oldName` is not found.

---

#### `CreateLogTableIfNotExists()`

```csharp
void CreateLogTableIfNotExists(
    this Migration self,
    string tableName,
    string schemaName = "dbo")
```

Creates `{tableName}_log` with standard audit columns:
- `id INT NOT NULL PRIMARY KEY IDENTITY`
- `timestamp DATETIME NULL`
- `username ANSISTRING(500) NOT NULL`
- `action ANSISTRING(50) NOT NULL`
- `record_id INT NOT NULL`

---

#### `CreateIndexIfNotExists()`

```csharp
IFluentSyntax? CreateIndexIfNotExists(
    this MigrationBase self,
    string tableName,
    string columnName,
    Func<ICreateIndexColumnOptionsSyntax, IFluentSyntax> configureIndex,
    string schemaName = "dbo")
```

Creates an index named `index_{columnName}` if it does not already exist.

---

#### `CreateCompositeIndexIfNotExists()`

```csharp
IFluentSyntax? CreateCompositeIndexIfNotExists(
    this MigrationBase self,
    string tableName,
    string[] columns,
    Func<ICreateIndexOnColumnSyntax, IFluentSyntax> configureIndex,
    string schemaName = "dbo",
    string? indexName = null)
```

Creates a composite index on multiple columns. Default name: `index_{col1}_{col2}_…`.

```csharp
this.CreateCompositeIndexIfNotExists("orders", ["user_id", "status"],
    idx => idx.WithOptions().Unique());
```

---

#### `DropIndexIfExists()`

```csharp
IFluentSyntax? DropIndexIfExists(
    this Migration self,
    string tableName,
    string columnName,
    string indexName,
    Func<IDeleteIndexOptionsSyntax, IFluentSyntax> configureDelete,
    string schemaName = "dbo")
```

Drops a named index only if it exists.

---

#### `CreateUniqueConstraintIfNotExists()`

```csharp
void CreateUniqueConstraintIfNotExists(
    this Migration self,
    string tableName,
    string constraintName,
    string[] columns,
    string schemaName = "dbo")
```

Creates a named UNIQUE constraint if it does not already exist.

```csharp
this.CreateUniqueConstraintIfNotExists("users", "uc_users_email", new[] { "email" });
```

---

#### `DropConstraintIfExists()`

```csharp
void DropConstraintIfExists(
    this Migration self,
    string tableName,
    string constraintName,
    string schemaName = "dbo")
```

Drops a named UNIQUE or CHECK constraint if it exists. Works on all databases supported by FluentMigrator.
For SQL Server DEFAULT constraints use `DropDefaultConstraintIfExists` from the SqlServer package.

---

#### `DropPrimaryKeyIfExists()`

```csharp
IFluentSyntax? DropPrimaryKeyIfExists(
    this Migration self,
    string tableName,
    string keyName,
    Func<IDeleteConstraintInSchemaOptionsSyntax, IFluentSyntax> configureDelete,
    string schemaName = "dbo")
```

Drops a primary key or unique constraint by name only if it exists.

---

#### `CreateSchemaIfNotExists()`

```csharp
void CreateSchemaIfNotExists(this Migration self, string schemaName)
```

Creates a schema if it does not already exist. Not supported on SQLite.

---

#### `DropSchemaIfExists()`

```csharp
void DropSchemaIfExists(this Migration self, string schemaName)
```

Drops a schema if it exists. Not supported on SQLite.

---

### SQL Server package (`TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer`)

```csharp
using FluentMigrator.IdempotentExtensions.SqlServer;
```

#### `DropDefaultConstraintIfExists()`

```csharp
void DropDefaultConstraintIfExists(
    this Migration self,
    string tableName,
    string columnName,
    string schemaName = "dbo")
```

Drops the `DEFAULT` constraint on `columnName` if one exists. Uses `sys.default_constraints` to locate the constraint by column — safe regardless of the constraint's auto-generated name.

**SQL Server / Azure SQL only.**

```csharp
// Remove the DEFAULT before altering the column type
this.DropDefaultConstraintIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```

---

## Database Compatibility

| Method | SQL Server | PostgreSQL | MySQL | SQLite |
|--------|:----------:|:----------:|:-----:|:------:|
| `WithIdColumn` | ✅ | ✅ | ✅ | ✅ |
| `CreateTableIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DropTableIfExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateColumnIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DeleteColumnIfExists` | ✅ | ✅ | ✅ | ✅ |
| `RenameColumnIfExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateLogTableIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateCompositeIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DropIndexIfExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateUniqueConstraintIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DropConstraintIfExists` | ✅ | ✅ | ✅ | ✅ |
| `DropPrimaryKeyIfExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateSchemaIfNotExists` | ✅ | ✅ | ✅ | ❌ |
| `DropSchemaIfExists` | ✅ | ✅ | ✅ | ❌ |
| **SqlServer package** | | | | |
| `DropDefaultConstraintIfExists` | ✅ | ❌ | ❌ | ❌ |

> **Note on `schemaName`:** The default value is `"dbo"` (SQL Server convention).
> For PostgreSQL use `"public"`, for MySQL omit the schema or pass the database name.
> For SQLite pass `""` (empty string) — SQLite has no schema support.

## License

MIT
