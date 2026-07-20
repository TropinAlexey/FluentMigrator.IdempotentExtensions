# FluentMigrator.IdempotentExtensions

Idempotent extension methods for [FluentMigrator](https://fluentmigrator.github.io/) migrations — safe to run multiple times on any database.

[![NuGet](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/)
[![NuGet downloads](https://img.shields.io/nuget/dt/TropinAlexey.FluentMigrator.IdempotentExtensions)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/)
[![NuGet (SqlServer)](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer?label=nuget%20%28SqlServer%29)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/)
[![CI](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml/badge.svg)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/github/license/TropinAlexey/FluentMigrator.IdempotentExtensions)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/blob/main/LICENSE)
[![GitHub tag](https://img.shields.io/github/v/tag/TropinAlexey/FluentMigrator.IdempotentExtensions)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/tags)

## Why

Regular FluentMigrator migrations assume every database starts from the same known state and are applied exactly once, in order. In practice, databases drift: manual hotfixes, partially-applied migrations, or instances that evolved independently all end up with different schemas even though they're supposed to be the same. Idempotent extensions let you write a migration that checks what already exists and only applies what's missing — so you can run it against any of those divergent databases and safely converge them all to the same target schema, instead of having to hand-reconcile each one first.

## What's New

### v1.5.0

- Added Oracle as a fifth supported provider. Most methods already worked via FluentMigrator's own fluent builder API (tables, columns, indexes, constraints, FKs, PKs, sequences, views); this release adds Oracle-specific branches only where raw SQL was needed: `AddColumnDefaultIfExists`/`DropColumnDefaultIfExists` (`ALTER TABLE ... MODIFY ... DEFAULT`), `DropTriggerIfExists`/`DropFunctionIfExists` (PL/SQL exception-swallow, since Oracle has no `DROP ... IF EXISTS`), `RenameIndexIfExists`/`RenameConstraintIfExists` (native `ALTER INDEX`/`ALTER TABLE ... RENAME CONSTRAINT`), and `InsertDataIfNotExists` (`FROM DUAL`, since Oracle has no FROM-less `SELECT`).
- `ResolveDefaultSchema` treats Oracle like MySQL/SQLite — schema defaults to `""` (Oracle schemas are the connected user, not a separate concept).
- Added Oracle to the Testcontainers integration suite (`gvenzl/oracle-free`), verifying the whole idempotency matrix against a real instance — confirmed the ORA-04080/ORA-04043 exception-swallow codes, `FROM DUAL`, and `ALTER INDEX`/`RENAME CONSTRAINT` all work as expected.
- Found and documented one genuine Oracle limitation this way: `AlterColumnIfExists` throws `ORA-01451` whenever `constructCol` calls `.Nullable()` on a column that's already nullable (Oracle rejects `MODIFY col ... NULL` when nullability doesn't change) — this can fail on the very first call, not just a rerun. An inherent Oracle restriction, not a library bug; a genuine nullability flip still works.
- Stays in the core provider-agnostic package — no new `.Oracle` package.

### v1.4.0

- `CreateCheckConstraintIfNotExists` — adds a named CHECK constraint if it doesn't already exist (not supported on SQLite). Drop it with the existing `DropConstraintIfExists`.
- `AddColumnDefaultIfExists` — sets a column's default value if the column exists (a no-op otherwise); companion to `DropColumnDefaultIfExists` (not supported on SQLite).
- `CreateViewIfNotExists` / `DropViewIfExists` — idempotent views across all four providers (`CREATE OR REPLACE VIEW` on PostgreSQL/MySQL, native `CREATE VIEW IF NOT EXISTS` on SQLite, an existence-guarded dynamic `CREATE VIEW` on SQL Server).
- `CreateTriggerIfNotExists` / `DropTriggerIfExists` — idempotent triggers across all four providers. Trigger bodies aren't portable SQL, so you supply the full provider-specific `CREATE TRIGGER` statement; these just make re-running it safe (`DropTriggerIfExists` first, then create).
- `CreateFunctionIfNotExists` / `DropFunctionIfExists` — idempotent SQL functions (SQL Server, PostgreSQL only; no general-purpose function feature on MySQL/SQLite). Same drop-then-create pattern as triggers. Mainly useful for PostgreSQL trigger functions, which must exist before a trigger can reference them.
- `RenameIndexIfExists` — renames an index if it exists (SQL Server, PostgreSQL, MySQL; not supported on SQLite, which has no rename-index DDL).
- `RenameConstraintIfExists` — renames a constraint if it exists (SQL Server, PostgreSQL only; MySQL only supports renaming indexes, not general constraints, and SQLite has neither).
- `UpdateDataIfExists` / `DeleteDataIfExists` — portable `UPDATE`/`DELETE` helpers matching rows by key columns, same value-formatting as `InsertDataIfNotExists`. Naturally idempotent on all four providers (an `UPDATE`/`DELETE` matching zero rows is always a safe no-op), so no existence guard is needed.
- All new methods stay in the core provider-agnostic package — no new `.Postgres`/`.MySql`/`.SQLite` packages.

### v1.3.1

- Fixed `CreatePrimaryKeyIfNotExists` on MySQL: the idempotency check looked for a constraint named after `keyName`, but MySQL always physically names primary key constraints `PRIMARY` — so the check never matched an existing key, and a second run failed with "Multiple primary key defined". Found by the new Testcontainers-based integration suite (see below).
- Added a real-database integration test suite (`tests/FluentMigrator.IdempotentExtensions.Tests.Integration`) that runs the full idempotency test matrix against actual SQL Server, PostgreSQL, and MySQL containers via [Testcontainers](https://testcontainers.com/), in addition to the existing SQLite tests. Runs in a separate `integration-tests` CI job so it doesn't slow down the main build.

### v1.3.0

- `CreateForeignKeyIfNotExists` / `DropForeignKeyIfExists` — idempotent foreign keys (SQL Server, PostgreSQL, MySQL; not supported on SQLite).
- `CreatePrimaryKeyIfNotExists` — add a primary key to an already-existing table (not supported on SQLite).
- `AlterColumnIfExists` — alter a column's type/constraints, guarded by a column-existence check (not supported on SQLite).
- `RenameTableIfExists` — rename a table only if the source table exists.
- `CreateSequenceIfNotExists` / `DropSequenceIfExists` — idempotent sequences (SQL Server, PostgreSQL; not supported on MySQL/SQLite). Raises the minimum `FluentMigrator` dependency to `6.*`.
- `DropColumnDefaultIfExists` — drops a column's default value on **any** provider (SQL Server, PostgreSQL, MySQL) from the core package, no SQL Server-only package required. The existing SqlServer-package `DropDefaultConstraintIfExists` is unchanged and still works.
- `InsertDataIfNotExists` — idempotent seed/reference-data inserts via a portable `INSERT ... WHERE NOT EXISTS` statement that works unmodified across all four providers. Correctly handles `null` key values (`IS NULL`, not `= NULL`), `Guid`, and `enum` values.
- All new methods were kept provider-agnostic in the core package rather than split into per-database packages, so no new `.Postgres`/`.MySql`/`.SQLite` packages were introduced.

## Packages

| Package | Description |
|---------|-------------|
| `TropinAlexey.FluentMigrator.IdempotentExtensions` | DB-agnostic helpers (SQL Server, PostgreSQL, MySQL, SQLite, Oracle) |
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

`CreateIndexIfNotExists`, `CreateCompositeIndexIfNotExists`, `CreateUniqueConstraintIfNotExists`, `CreatePrimaryKeyIfNotExists`, and `CreateForeignKeyIfNotExists` let you introduce a performance index, uniqueness rule, primary key, or relationship without checking whether a previous migration, hotfix, or DBA already created it.

#### Idempotent seed/reference data

`InsertDataIfNotExists` inserts a lookup-table row (statuses, roles, feature flags, …) only if a row matching its key columns isn't already there — safe to re-run alongside the rest of the migration instead of needing a separate one-time seeding script.

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
    string? schemaName = null)
```

Creates a table only if it does not already exist. Returns `null` if already exists.

---

#### `DropTableIfExists()`

```csharp
void DropTableIfExists(
    this Migration self,
    string tableName,
    string? schemaName = null)
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
    string? schemaName = null)
```

Adds a column to an existing table only if that column does not exist. Returns `null` if the table or column is already present.

---

#### `AlterColumnIfExists()`

```csharp
IFluentSyntax? AlterColumnIfExists(
    this Migration self,
    string tableName,
    string colName,
    Func<IAlterTableColumnAsTypeSyntax, IFluentSyntax> constructCol,
    string? schemaName = null)
```

Alters an existing column only if it's present. Returns `null` if the table or column does not exist. Only guards existence — it does not diff the current column definition against the target one, so `constructCol` always runs when the column is present. **Not supported on SQLite** (no native `ALTER COLUMN`).

**Oracle caveat:** a `constructCol` that calls `.Nullable()` on a column that's already nullable throws `ORA-01451` — Oracle rejects `MODIFY col ... NULL` when nullability doesn't change. This can fail on the very first call, not just a rerun. Only affects calls that leave nullability unchanged; a genuine nullability flip (`NotNullable()` ↔ `Nullable()`) works normally.

---

#### `DropColumnDefaultIfExists()`

```csharp
void DropColumnDefaultIfExists(
    this Migration self,
    string tableName,
    string columnName,
    string? schemaName = null)
```

Drops the default value on a column, on **any** provider. On SQL Server, DEFAULT constraints are auto-named objects, so this locates the actual constraint via `sys.default_constraints` and drops it with a single conditional T-SQL block. On PostgreSQL and MySQL, `ALTER COLUMN ... DROP DEFAULT` is itself a no-op when no default is set, so it runs directly. Not supported on SQLite.

```csharp
this.DropColumnDefaultIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```

---

#### `DeleteColumnIfExists()`

```csharp
void DeleteColumnIfExists(
    this Migration self,
    string tableName,
    string colName,
    string? schemaName = null)
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
    string? schemaName = null)
```

Renames a column only if the source column exists. No-op if `oldName` is not found.

---

#### `RenameTableIfExists()`

```csharp
void RenameTableIfExists(
    this Migration self,
    string oldName,
    string newName,
    string? schemaName = null)
```

Renames a table only if the source table exists. No-op if `oldName` is not found.

---

#### `CreateLogTableIfNotExists()`

```csharp
void CreateLogTableIfNotExists(
    this Migration self,
    string tableName,
    string? schemaName = null)
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
    string? schemaName = null)
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
    string? schemaName = null,
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
    string? schemaName = null)
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
    string? schemaName = null)
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
    string? schemaName = null)
```

Drops a named UNIQUE or CHECK constraint if it exists. Works on all databases supported by FluentMigrator.
For default values use `DropColumnDefaultIfExists`.

---

#### `DropPrimaryKeyIfExists()`

```csharp
IFluentSyntax? DropPrimaryKeyIfExists(
    this Migration self,
    string tableName,
    string keyName,
    Func<IDeleteConstraintInSchemaOptionsSyntax, IFluentSyntax> configureDelete,
    string? schemaName = null)
```

Drops a primary key or unique constraint by name only if it exists.

---

#### `CreatePrimaryKeyIfNotExists()`

```csharp
void CreatePrimaryKeyIfNotExists(
    this Migration self,
    string tableName,
    string keyName,
    string[] columns,
    string? schemaName = null)
```

Creates a named PRIMARY KEY constraint if it does not already exist — useful for adding a primary key to a table that was created without one (e.g. legacy tables). **Not supported on SQLite.**

---

#### `CreateForeignKeyIfNotExists()`

```csharp
void CreateForeignKeyIfNotExists(
    this Migration self,
    string tableName,
    string foreignKeyName,
    string[] foreignColumns,
    string primaryTableName,
    string[] primaryColumns,
    string? schemaName = null,
    string? primarySchemaName = null)
```

Creates a foreign key from `tableName` to `primaryTableName` if it does not already exist. `primarySchemaName` defaults to `schemaName` when omitted. **Not supported on SQLite.**

```csharp
this.CreateForeignKeyIfNotExists("orders", "fk_orders_users", new[] { "user_id" }, "users", new[] { "id" });
```

---

#### `DropForeignKeyIfExists()`

```csharp
void DropForeignKeyIfExists(
    this Migration self,
    string tableName,
    string foreignKeyName,
    string? schemaName = null)
```

Drops a named foreign key if it exists. **Not supported on SQLite.**

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

#### `CreateSequenceIfNotExists()`

```csharp
void CreateSequenceIfNotExists(
    this Migration self,
    string sequenceName,
    Action<ICreateSequenceSyntax>? configureSequence = null,
    string? schemaName = null)
```

Creates a sequence if it does not already exist. `configureSequence` lets you set increment, min/max, start value, caching and cycling. **Not supported on MySQL/MariaDB or SQLite.**

```csharp
this.CreateSequenceIfNotExists("order_number_seq", s => s.StartWith(1000).IncrementBy(1));
```

---

#### `DropSequenceIfExists()`

```csharp
void DropSequenceIfExists(
    this Migration self,
    string sequenceName,
    string? schemaName = null)
```

Drops a sequence if it exists. **Not supported on MySQL/MariaDB or SQLite.**

---

#### `InsertDataIfNotExists()`

```csharp
void InsertDataIfNotExists(
    this Migration self,
    string tableName,
    IReadOnlyDictionary<string, object?> keyValues,
    IReadOnlyDictionary<string, object?>? additionalValues = null,
    string? schemaName = null)
```

Inserts a row only if no row matching `keyValues` already exists — for idempotently seeding small reference/lookup tables. Uses a single portable `INSERT ... SELECT ... WHERE NOT EXISTS (...)` statement that runs unmodified on SQL Server, PostgreSQL, MySQL and SQLite. `keyValues` must have at least one entry. Values are formatted as SQL literals: strings are quote-escaped, `null` key values use `IS NULL` (not `= NULL`, which never matches), booleans become `1`/`0`, `Guid` is quoted, and enums use their underlying numeric value. Table/column identifiers are **not** quoted, so avoid reserved words.

```csharp
this.InsertDataIfNotExists("statuses",
    keyValues: new Dictionary<string, object?> { ["code"] = "ACTIVE" },
    additionalValues: new Dictionary<string, object?> { ["label"] = "Active" });
```

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

**SQL Server / Azure SQL only.** Kept for backward compatibility — new code targeting multiple providers should use `DropColumnDefaultIfExists` from the core package instead, which covers SQL Server, PostgreSQL and MySQL with one call.

```csharp
// Remove the DEFAULT before altering the column type
this.DropDefaultConstraintIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```

---

## Database Compatibility

| Method | SQL Server | PostgreSQL | MySQL | SQLite | Oracle |
|--------|:----------:|:----------:|:-----:|:------:|:------:|
| `WithIdColumn` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateTableIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropTableIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateColumnIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DeleteColumnIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `RenameColumnIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateLogTableIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateCompositeIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropIndexIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateUniqueConstraintIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropConstraintIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropPrimaryKeyIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreatePrimaryKeyIfNotExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `CreateForeignKeyIfNotExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropForeignKeyIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `AlterColumnIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropColumnDefaultIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `RenameTableIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateSchemaIfNotExists` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `DropSchemaIfExists` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `CreateSequenceIfNotExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `DropSequenceIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `InsertDataIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| **SqlServer package** | | | | | |
| `DropDefaultConstraintIfExists` | ✅ | ❌ | ❌ | ❌ | ❌ |

> **Note on `schemaName`:** When omitted (or `null`), it's auto-detected from the current
> database provider: `"dbo"` for SQL Server, `"public"` for PostgreSQL, `""` for MySQL,
> SQLite and Oracle (SQLite has no schema support; MySQL treats schema as the connection's
> database; Oracle schemas are the connected user, not a separate concept). Pass an explicit
> value to override — e.g. for multi-tenant setups where each migration run targets a
> different schema.
>
> The one exception is `DropDefaultConstraintIfExists` (SqlServer package), which is SQL
> Server-only and keeps a plain `"dbo"` default.

> **Note on FluentMigrator version:** the core package requires `FluentMigrator 6.*` or later
> (raised from `5.*` in v1.3.0) to access the schema-existence check used by
> `CreateSequenceIfNotExists`/`DropSequenceIfExists`.

## License

MIT
