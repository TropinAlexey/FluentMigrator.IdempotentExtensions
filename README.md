<p align="center">
  <h1 align="center">FluentMigrator.IdempotentExtensions</h1>
  <p align="center">
    Idempotent extension methods for <a href="https://fluentmigrator.github.io/">FluentMigrator</a> — safe to run multiple times on any database.
  </p>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/"><img src="https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions?style=flat-square&logo=nuget&label=NuGet" alt="NuGet"></a>
  <a href="https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/"><img src="https://img.shields.io/nuget/dt/TropinAlexey.FluentMigrator.IdempotentExtensions?style=flat-square&logo=nuget&label=Downloads" alt="NuGet downloads"></a>
  <a href="https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/"><img src="https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer?style=flat-square&logo=nuget&label=SqlServer" alt="NuGet SqlServer"></a>
  <br/>
  <a href="https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/TropinAlexey/FluentMigrator.IdempotentExtensions/ci.yml?style=flat-square&logo=github&label=CI" alt="CI"></a>
  <a href="https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/blob/main/LICENSE"><img src="https://img.shields.io/github/license/TropinAlexey/FluentMigrator.IdempotentExtensions?style=flat-square" alt="License"></a>
  <a href="https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/tags"><img src="https://img.shields.io/github/v/tag/TropinAlexey/FluentMigrator.IdempotentExtensions?style=flat-square&label=Latest%20Tag" alt="GitHub tag"></a>
  <img src="https://img.shields.io/badge/.NET%20Standard-2.0-512bd4?style=flat-square&logo=dotnet" alt=".NET Standard 2.0">
</p>

---

## Why

Regular FluentMigrator migrations assume every database starts from the same known state and are applied exactly once, in order. In practice, databases drift: manual hotfixes, partially-applied migrations, or instances that evolved independently all end up with different schemas even though they're supposed to be the same.

Idempotent extensions let you write a migration that **checks what already exists** and **only applies what's missing** — so you can run it against any of those divergent databases and safely converge them all to the same target schema, instead of having to hand-reconcile each one first.

## Supported Databases

| | SQL Server | PostgreSQL | MySQL | SQLite | Oracle |
|---|:---:|:---:|:---:|:---:|:---:|
| **40+ methods** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Testcontainers tests** | ✅ | ✅ | ✅ | — | ✅ |

## Packages

| Package | Description |
|---------|-------------|
| [`TropinAlexey.FluentMigrator.IdempotentExtensions`](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/) | DB-agnostic helpers (SQL Server, PostgreSQL, MySQL, SQLite, Oracle) |
| [`TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer`](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/) | SQL Server / Azure SQL specific helpers |

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
            .WithIdColumn()
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

        // Idempotent seed data — anonymous objects, just like FluentMigrator's own API
        this.InsertDataIfNotExists("users",
            keyValues: new { email = "admin@example.com" },
            additionalValues: new { name = "Admin" });
    }

    public override void Down()
    {
        this.DropTableIfExists("users");
    }
}
```

### Full tour — every method with an example

Below is every method in the core package, grouped by topic. `schemaName` is omitted everywhere for brevity — it is auto-detected per provider (`dbo` on SQL Server, `public` on PostgreSQL, empty on MySQL/SQLite/Oracle); pass it explicitly for multi-tenant setups. Provider limitations are noted inline.

**Tables**

```csharp
// Rename a table, create an audit log table
this.RenameTableIfExists("users", "app_users");
this.CreateLogTableIfNotExists("app_users"); // creates app_users_log(id, timestamp, username, action, record_id)

// Drop only if it exists — the standard Down() counterpart
this.DropTableIfExists("app_users");
```

**Columns**

```csharp
this.AlterColumnIfExists("users", "email", c => c.AsString(1000).Nullable()); // retype / resize
this.RenameColumnIfExists("users", "name", "full_name");
this.DeleteColumnIfExists("users", "legacy_flag");
```

**Indexes**

```csharp
this.CreateCompositeIndexIfNotExists("orders", new[] { "user_id", "status" },
    idx => idx.WithOptions().NonClustered());

this.DropIndexIfExists("users", "email", "index_email", d => d); // no-op when missing
this.RenameIndexIfExists("users", "index_email", "idx_users_email"); // not on SQLite
```

**Constraints & keys**

```csharp
this.CreateCheckConstraintIfNotExists("users", "ck_users_age", "age >= 0"); // not on SQLite
this.CreatePrimaryKeyIfNotExists("legacy_orders", "pk_legacy_orders", new[] { "code" }); // not on SQLite
this.DropPrimaryKeyIfExists("legacy_orders", "pk_legacy_orders", d => d); // no-op when missing

this.CreateForeignKeyIfNotExists("orders", "fk_orders_users",
    new[] { "user_id" }, "users", new[] { "id" }); // not on SQLite
this.DropForeignKeyIfExists("orders", "fk_orders_users");

this.DropConstraintIfExists("users", "ck_users_age"); // drops UNIQUE or CHECK by name
this.RenameConstraintIfExists("users", "uc_users_email", "uc_users_email_v2"); // SQL Server, PostgreSQL, Oracle
```

**Column defaults**

```csharp
this.AddColumnDefaultIfExists("users", "status", 0); // set DEFAULT only if the column exists; not on SQLite
this.DropColumnDefaultIfExists("users", "status"); // any provider, incl. auto-named SQL Server constraints
```

**Schemas**

```csharp
this.CreateSchemaIfNotExists("billing");
this.DropSchemaIfExists("billing_old"); // not on SQLite or Oracle
```

**Sequences** (not on MySQL/MariaDB or SQLite)

```csharp
this.CreateSequenceIfNotExists("order_number_seq", s => s.StartWith(1000).IncrementBy(1));
this.AlterSequenceIfExists("order_number_seq", incrementBy: 5, maxValue: 10000);
this.DropSequenceIfExists("order_number_seq");
```

**Data** (anonymous objects, just like FluentMigrator's own API)

```csharp
this.UpsertData("statuses",
    keyValues: new { code = "ACTIVE" },
    additionalValues: new { label = "Active" }); // INSERT or UPDATE; needs UNIQUE key on PG/MySQL/SQLite

this.UpdateDataIfExists("statuses",
    keyValues: new { code = "ACTIVE" },
    setValues: new { label = "Enabled" }); // zero-match is a safe no-op

this.DeleteDataIfExists("statuses", new { code = "DEPRECATED" });
```

**Views, triggers, functions**

```csharp
this.CreateViewIfNotExists("active_users", "SELECT id, email FROM users WHERE active = 1");
this.CreateOrReplaceView("active_users", "SELECT id, email, name FROM users WHERE active = 1"); // redefine
this.DropViewIfExists("active_users");

// Trigger body is provider-specific — you supply the full CREATE TRIGGER, the method makes re-running safe
this.CreateTriggerIfNotExists("trg_users_audit", "users",
    "CREATE TRIGGER trg_users_audit ON users AFTER INSERT AS BEGIN ... END");
this.DropTriggerIfExists("trg_users_audit", "users");

// Functions: SQL Server, PostgreSQL, Oracle (e.g. a Postgres trigger function must exist first)
this.CreateFunctionIfNotExists("touch_updated_at",
    "CREATE FUNCTION touch_updated_at() RETURNS trigger AS $$ BEGIN ... END; $$ LANGUAGE plpgsql;");
this.DropFunctionIfExists("touch_updated_at");
```

**Conditional SQL** — escape hatch (SQL Server, PostgreSQL, Oracle)

```csharp
this.ExecuteSqlIfExists(
    "SELECT 1 FROM sys.server_principals WHERE name = 'app_user'",
    "ALTER LOGIN app_user DISABLE");

this.ExecuteSqlIfNotExists(
    "SELECT 1 FROM sys.server_principals WHERE name = 'app_user'",
    "CREATE LOGIN app_user WITH PASSWORD = 'secret'");
```

**Maintenance** — pure upkeep, safe to re-run every deploy, empty `Down()`

```csharp
this.ReorganizeIndexes("client_appointments");
this.UpdateStatistics("client_appointments", samplePercent: 30); // sampling honored on SQL Server + Oracle
```

**SQL Server-only package** (`TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer`)

```csharp
using FluentMigrator.IdempotentExtensions.SqlServer;

this.DropDefaultConstraintIfExists("users", "status"); // finds the auto-named DEFAULT via sys.default_constraints
```

## Use Cases

<details>
<summary><b>Idempotent CI/CD deploys</b></summary>

Migration runners are re-invoked on retries, rolling restarts, and repeated pipeline runs. Every method here checks existence first, so the same migration can be applied any number of times without ever failing on "table/column/index already exists".
</details>

<details>
<summary><b>Expand-contract schema changes</b></summary>

Add a new column, backfill it in application code, then drop the old one in a later release — using `CreateColumnIfNotExists` and `DeleteColumnIfExists` for each step. If a deploy fails partway and the migration re-runs, already-applied steps are skipped instead of throwing.
</details>

<details>
<summary><b>Renaming columns across environments at different versions</b></summary>

`RenameColumnIfExists` only renames when the old column name still exists. Useful when dev/staging/prod (or per-tenant databases) aren't all on the same migration checkpoint — environments where a manual fix already renamed the column are left alone.
</details>

<details>
<summary><b>Idempotent seed/reference data</b></summary>

`InsertDataIfNotExists` inserts a lookup-table row (statuses, roles, feature flags, …) only if a row matching its key columns isn't already there — safe to re-run alongside the rest of the migration.
</details>

<details>
<summary><b>Multi-tenant / multi-schema databases</b></summary>

Every method accepts `schemaName`, so the same migration class can loop over several tenant schemas in one pass instead of duplicating migration logic per schema.
</details>

<details>
<summary><b>SQL Server: altering a column blocked by an auto-named DEFAULT constraint</b></summary>

SQL Server auto-generates DEFAULT constraint names, so you often can't `DROP CONSTRAINT` by a name you control before an `AlterColumn`. `DropDefaultConstraintIfExists` (SqlServer package) locates and drops it by column name instead.

```csharp
this.DropDefaultConstraintIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```
</details>

<details>
<summary><b>Regular index & statistics maintenance</b></summary>

`ReorganizeIndexes` + `UpdateStatistics` are pure maintenance — no schema changes — so they are safe to re-run on every deploy. Pass table names as parameters (nothing is hardcoded) and loop over as many tables as you need:

```csharp
foreach (var table in new[] { "client_appointments", "client_cash_income", "client_appointment_result_procedures" })
{
    this.ReorganizeIndexes(table);
    this.UpdateStatistics(table, samplePercent: 30);
}
```

On SQL Server this is exactly equivalent to your familiar snippet:

```sql
ALTER INDEX ALL ON client_appointments REORGANIZE;
UPDATE STATISTICS client_appointments WITH SAMPLE 30 PERCENT;
```

…but the same C# code also works on PostgreSQL, MySQL, SQLite, and Oracle (see the **Maintenance** section in API Reference for the per-database SQL mapping). `Down()` stays empty — there is nothing to roll back.
</details>

## API Reference

### Core package

<details>
<summary><b>Tables</b> — <code>CreateTableIfNotExists</code>, <code>DropTableIfExists</code>, <code>RenameTableIfExists</code>, <code>CreateLogTableIfNotExists</code></summary>

#### `CreateTableIfNotExists()`

```csharp
IFluentSyntax? CreateTableIfNotExists(
    this Migration self,
    string tableName,
    Func<ICreateTableWithColumnOrSchemaOrDescriptionSyntax, IFluentSyntax> constructTable,
    string? schemaName = null)
```

Creates a table only if it does not already exist. Returns `null` if already exists.

#### `DropTableIfExists()`

```csharp
void DropTableIfExists(
    this Migration self,
    string tableName,
    string? schemaName = null)
```

Drops a table only if it exists. No-op otherwise.

#### `RenameTableIfExists()`

```csharp
void RenameTableIfExists(
    this Migration self,
    string oldName,
    string newName,
    string? schemaName = null)
```

Renames a table only if the source table exists. No-op if `oldName` is not found.

#### `CreateLogTableIfNotExists()`

```csharp
void CreateLogTableIfNotExists(
    this Migration self,
    string tableName,
    string? schemaName = null)
```

Creates `{tableName}_log` with standard audit columns: `id`, `timestamp`, `username`, `action`, `record_id`.

</details>

<details>
<summary><b>Columns</b> — <code>WithIdColumn</code>, <code>CreateColumnIfNotExists</code>, <code>AlterColumnIfExists</code>, <code>DeleteColumnIfExists</code>, <code>RenameColumnIfExists</code></summary>

#### `WithIdColumn()`

```csharp
ICreateTableColumnOptionOrWithColumnSyntax WithIdColumn(
    this ICreateTableWithColumnSyntax tableWithColumnSyntax)
```

Adds `id INT NOT NULL PRIMARY KEY IDENTITY` to a `Create.Table(...)` chain.

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

#### `AlterColumnIfExists()`

```csharp
IFluentSyntax? AlterColumnIfExists(
    this Migration self,
    string tableName,
    string colName,
    Func<IAlterTableColumnAsTypeSyntax, IFluentSyntax> constructCol,
    string? schemaName = null)
```

Alters an existing column only if it's present. Only guards existence — it does not diff the current column definition against the target one, so `constructCol` always runs when the column is present. **Not supported on SQLite.** **Oracle caveat:** `.Nullable()` on an already-nullable column throws `ORA-01451`.

#### `DeleteColumnIfExists()`

```csharp
void DeleteColumnIfExists(
    this Migration self,
    string tableName,
    string colName,
    string? schemaName = null)
```

Drops a column only if it exists. No-op otherwise.

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

</details>

<details>
<summary><b>Indexes</b> — <code>CreateIndexIfNotExists</code>, <code>CreateCompositeIndexIfNotExists</code>, <code>DropIndexIfExists</code>, <code>RenameIndexIfExists</code></summary>

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

#### `RenameIndexIfExists()`

Renames an index if it exists. **Not supported on SQLite.**

</details>

<details>
<summary><b>Constraints & Keys</b> — <code>CreateUniqueConstraintIfNotExists</code>, <code>DropConstraintIfExists</code>, <code>CreateCheckConstraintIfNotExists</code>, <code>CreatePrimaryKeyIfNotExists</code>, <code>DropPrimaryKeyIfExists</code>, <code>CreateForeignKeyIfNotExists</code>, <code>DropForeignKeyIfExists</code>, <code>RenameConstraintIfExists</code></summary>

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

#### `DropConstraintIfExists()`

```csharp
void DropConstraintIfExists(
    this Migration self,
    string tableName,
    string constraintName,
    string? schemaName = null)
```

Drops a named UNIQUE or CHECK constraint if it exists. For default values use `DropColumnDefaultIfExists`.

#### `CreateCheckConstraintIfNotExists()`

Adds a named CHECK constraint if it doesn't already exist. **Not supported on SQLite.**

#### `CreatePrimaryKeyIfNotExists()`

```csharp
void CreatePrimaryKeyIfNotExists(
    this Migration self,
    string tableName,
    string keyName,
    string[] columns,
    string? schemaName = null)
```

Creates a named PRIMARY KEY constraint if it does not already exist. **Not supported on SQLite.**

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

Creates a foreign key if it does not already exist. **Not supported on SQLite.**

```csharp
this.CreateForeignKeyIfNotExists("orders", "fk_orders_users",
    new[] { "user_id" }, "users", new[] { "id" });
```

#### `DropForeignKeyIfExists()`

```csharp
void DropForeignKeyIfExists(
    this Migration self,
    string tableName,
    string foreignKeyName,
    string? schemaName = null)
```

Drops a named foreign key if it exists. **Not supported on SQLite.**

#### `RenameConstraintIfExists()`

Renames a constraint if it exists. **SQL Server, PostgreSQL, and Oracle.**

</details>

<details>
<summary><b>Defaults</b> — <code>AddColumnDefaultIfExists</code>, <code>DropColumnDefaultIfExists</code></summary>

#### `AddColumnDefaultIfExists()`

Sets a column's default value if the column exists (a no-op otherwise). **Not supported on SQLite.**

#### `DropColumnDefaultIfExists()`

```csharp
void DropColumnDefaultIfExists(
    this Migration self,
    string tableName,
    string columnName,
    string? schemaName = null)
```

Drops the default value on a column, on **any** provider. On SQL Server, locates the auto-named DEFAULT constraint via `sys.default_constraints`. On PostgreSQL/MySQL, `ALTER COLUMN ... DROP DEFAULT` is itself a no-op when no default is set. **Not supported on SQLite.**

```csharp
this.DropColumnDefaultIfExists("users", "status");
Alter.Table("users").AlterColumn("status").AsInt32().NotNullable();
```

</details>

<details>
<summary><b>Schemas</b> — <code>CreateSchemaIfNotExists</code>, <code>DropSchemaIfExists</code></summary>

#### `CreateSchemaIfNotExists()`

```csharp
void CreateSchemaIfNotExists(this Migration self, string schemaName)
```

Creates a schema if it does not already exist. **Not supported on SQLite or Oracle.**

#### `DropSchemaIfExists()`

```csharp
void DropSchemaIfExists(this Migration self, string schemaName)
```

Drops a schema if it exists. **Not supported on SQLite or Oracle.**

</details>

<details>
<summary><b>Sequences</b> — <code>CreateSequenceIfNotExists</code>, <code>AlterSequenceIfExists</code>, <code>DropSequenceIfExists</code></summary>

#### `CreateSequenceIfNotExists()`

```csharp
void CreateSequenceIfNotExists(
    this Migration self,
    string sequenceName,
    Action<ICreateSequenceSyntax>? configureSequence = null,
    string? schemaName = null)
```

Creates a sequence if it does not already exist. **Not supported on MySQL/MariaDB or SQLite.**

```csharp
this.CreateSequenceIfNotExists("order_number_seq",
    s => s.StartWith(1000).IncrementBy(1));
```

#### `AlterSequenceIfExists()`

```csharp
void AlterSequenceIfExists(
    this Migration self,
    string sequenceName,
    long? incrementBy = null,
    long? minValue = null,
    long? maxValue = null,
    long? restartWith = null,
    long? startWith = null,
    long? cache = null,
    bool? cycle = null,
    string? schemaName = null)
```

Alters a sequence if it exists; no-op otherwise. Provider differences are handled internally (e.g. Oracle uses `NOCACHE`/`NOCYCLE` instead of `NO CACHE`/`NO CYCLE`, and `START WITH` instead of `RESTART WITH`). **Not supported on MySQL/MariaDB or SQLite.**

```csharp
this.AlterSequenceIfExists("order_number_seq", incrementBy: 5, maxValue: 10000);
```

#### `DropSequenceIfExists()`

```csharp
void DropSequenceIfExists(
    this Migration self,
    string sequenceName,
    string? schemaName = null)
```

Drops a sequence if it exists. **Not supported on MySQL/MariaDB or SQLite.**

</details>

<details>
<summary><b>Data</b> — <code>InsertDataIfNotExists</code>, <code>UpsertData</code>, <code>UpdateDataIfExists</code>, <code>DeleteDataIfExists</code></summary>

#### `InsertDataIfNotExists()`

Inserts a row only if no row matching `keyValues` already exists. Uses a single portable `INSERT ... SELECT ... WHERE NOT EXISTS (...)` statement. Handles `null` (`IS NULL`), `Guid`, `bool` (→ `1`/`0`), and `enum` (→ numeric) values.

All data methods accept both **anonymous objects** (recommended) and `IReadOnlyDictionary<string, object?>`:

```csharp
// anonymous object — clean, FluentMigrator-style API
this.InsertDataIfNotExists("statuses",
    keyValues: new { code = "ACTIVE" },
    additionalValues: new { label = "Active" });

// dictionary — still supported for dynamic scenarios
this.InsertDataIfNotExists("statuses",
    keyValues: new Dictionary<string, object?> { ["code"] = "ACTIVE" },
    additionalValues: new Dictionary<string, object?> { ["label"] = "Active" });
```

#### `UpsertData()`

Inserts a row if no row matching `keyValues` exists; otherwise updates the matching row with `additionalValues`. Uses provider-specific syntax: `MERGE` on SQL Server/Oracle, `ON CONFLICT ... DO UPDATE` on PostgreSQL/SQLite, `ON DUPLICATE KEY UPDATE` on MySQL. PostgreSQL, MySQL, and SQLite require a UNIQUE constraint (or PRIMARY KEY) on the key columns.

```csharp
this.UpsertData("statuses",
    keyValues: new { code = "ACTIVE" },
    additionalValues: new { label = "Active" });
```

#### `UpdateDataIfExists()` / `DeleteDataIfExists()`

Portable `UPDATE`/`DELETE` helpers matching rows by key columns. Naturally idempotent on all providers (zero-match is a no-op).

```csharp
this.UpdateDataIfExists("statuses",
    keyValues: new { code = "ACTIVE" },
    setValues: new { label = "Enabled" });

this.DeleteDataIfExists("statuses", new { code = "DEPRECATED" });
```

</details>

<details>
<summary><b>Views & Triggers & Functions</b></summary>

#### `CreateViewIfNotExists()` / `DropViewIfExists()`

Idempotent views across all five providers.

#### `CreateOrReplaceView()`

```csharp
void CreateOrReplaceView(
    this Migration self,
    string viewName,
    string selectSql,
    string? schemaName = null)
```

Drops and recreates a view in a single call — equivalent to `DropViewIfExists` + `CreateViewIfNotExists`. Use when the view definition has changed between releases.

#### `CreateTriggerIfNotExists()` / `DropTriggerIfExists()`

Idempotent triggers across all five providers. You supply the full `CREATE TRIGGER` body; these methods make re-running it safe.

#### `CreateFunctionIfNotExists()` / `DropFunctionIfExists()`

Idempotent SQL functions. **SQL Server, PostgreSQL, and Oracle.** Mainly useful for PostgreSQL trigger functions.

</details>

<details>
<summary><b>Conditional SQL</b> — <code>ExecuteSqlIfExists</code>, <code>ExecuteSqlIfNotExists</code></summary>

#### `ExecuteSqlIfExists()`

```csharp
void ExecuteSqlIfExists(
    this Migration self,
    string conditionSql,
    string executeSql)
```

Executes `executeSql` only if `conditionSql` returns at least one row. An escape hatch for idempotent operations not covered by the specialized methods. **SQL Server, PostgreSQL, and Oracle only.**

#### `ExecuteSqlIfNotExists()`

```csharp
void ExecuteSqlIfNotExists(
    this Migration self,
    string conditionSql,
    string executeSql)
```

Executes `executeSql` only if `conditionSql` returns no rows. **SQL Server, PostgreSQL, and Oracle only.**

```csharp
this.ExecuteSqlIfNotExists(
    "SELECT 1 FROM sys.server_principals WHERE name = 'app_user'",
    "CREATE LOGIN app_user WITH PASSWORD = 'secret'");
```

</details>

<details>
<summary><b>Maintenance</b> — <code>ReorganizeIndexes</code>, <code>UpdateStatistics</code></summary>

Pure maintenance operations: defragment indexes and refresh optimizer statistics. No schema changes, so both methods are safe to re-run on every deploy. Tables are always passed as parameters — nothing is hardcoded. If a table does not exist, the statement fails server-side (no silent skip).

```csharp
// Defragment all indexes on a table
void ReorganizeIndexes(
    this Migration self,
    string tableName,
    string? schemaName = null)

// Refresh optimizer statistics; samplePercent is 1-100, null = server default
void UpdateStatistics(
    this Migration self,
    string tableName,
    int? samplePercent = 30,
    string? schemaName = null)
```

```csharp
[Migration(20240501)]
public class MaintenanceClientTables : Migration
{
    public override void Up()
    {
        foreach (var table in new[] { "client_appointments", "client_cash_income", "client_appointment_result_procedures" })
        {
            this.ReorganizeIndexes(table);
            this.UpdateStatistics(table, samplePercent: 30);
        }
    }

    // Maintenance has nothing to roll back.
    public override void Down() { }
}
```

What each method actually executes per database:

| Method | SQL Server | PostgreSQL | MySQL / MariaDB | SQLite | Oracle |
|--------|:---:|:---:|:---:|:---:|:---:|
| `ReorganizeIndexes("t")` | `ALTER INDEX ALL ON [dbo].[t] REORGANIZE;` | `REINDEX TABLE public.t;` | `OPTIMIZE TABLE t;` (also refreshes stats) | `REINDEX t;` | PL/SQL loop: `ALTER INDEX ... REBUILD` for every index on `t` |
| `UpdateStatistics("t", 30)` | `UPDATE STATISTICS [dbo].[t] WITH SAMPLE 30 PERCENT;` | `ANALYZE public.t;` | `ANALYZE TABLE t;` | `ANALYZE t;` | `DBMS_STATS.GATHER_TABLE_STATS(..., estimate_percent => 30);` |

Notes:

- `samplePercent` is honored **only on SQL Server and Oracle**. PostgreSQL, MySQL, and SQLite have no per-table sampling clause, so the parameter is ignored there (their `ANALYZE` uses server defaults).
- Pass `samplePercent: null` for server-default behavior everywhere: no sampling clause on SQL Server, `DBMS_STATS.AUTO_SAMPLE_SIZE` on Oracle.
- Values outside 1–100 throw `ArgumentOutOfRangeException` immediately, before any SQL runs.

</details>

### SQL Server package

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

Drops the `DEFAULT` constraint on a column by locating it via `sys.default_constraints`. **SQL Server / Azure SQL only.** New code targeting multiple providers should use `DropColumnDefaultIfExists` from the core package instead.

## Database Compatibility

| Method | SQL Server | PostgreSQL | MySQL | SQLite | Oracle |
|--------|:---:|:---:|:---:|:---:|:---:|
| `CreateTableIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropTableIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `RenameTableIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateColumnIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `AlterColumnIfExists` | ✅ | ✅ | ✅ | ❌ | ⚠️ |
| `DeleteColumnIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `RenameColumnIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateCompositeIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropIndexIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `RenameIndexIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `CreateUniqueConstraintIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateCheckConstraintIfNotExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropConstraintIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `RenameConstraintIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `CreatePrimaryKeyIfNotExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropPrimaryKeyIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateForeignKeyIfNotExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropForeignKeyIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `AddColumnDefaultIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `DropColumnDefaultIfExists` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `CreateSchemaIfNotExists` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `DropSchemaIfExists` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `CreateSequenceIfNotExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `AlterSequenceIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `DropSequenceIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `InsertDataIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `UpsertData` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `UpdateDataIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DeleteDataIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateViewIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropViewIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateTriggerIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `DropTriggerIfExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateFunctionIfNotExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `DropFunctionIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `CreateLogTableIfNotExists` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CreateOrReplaceView` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `ExecuteSqlIfExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `ExecuteSqlIfNotExists` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `ReorganizeIndexes` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `UpdateStatistics` | ✅ | ✅¹ | ✅¹ | ✅¹ | ✅ |
| `WithIdColumn` | ✅ | ✅ | ✅ | ✅ | ✅ |
| **SqlServer package** |   |   |   |   |   |
| `DropDefaultConstraintIfExists` | ✅ | ❌ | ❌ | ❌ | ❌ |

> ⚠️ `AlterColumnIfExists` on Oracle: `.Nullable()` on an already-nullable column throws `ORA-01451` — an inherent Oracle restriction, not a library bug.
>
> ¹ `UpdateStatistics` runs everywhere, but `samplePercent` is honored only on SQL Server and Oracle — PostgreSQL, MySQL, and SQLite have no per-table sampling clause, so their `ANALYZE` uses server defaults.

> **`schemaName` auto-detection:** When omitted, defaults to `"dbo"` for SQL Server, `"public"` for PostgreSQL, `""` for MySQL, SQLite, and Oracle (SQLite has no schema support; MySQL treats schema as the connection's database; Oracle schemas are the connected user, not a separate concept). Pass an explicit value for multi-tenant setups.
>
> The one exception is `DropDefaultConstraintIfExists` (SqlServer package), which is SQL Server-only and keeps a plain `"dbo"` default.

> **FluentMigrator version:** requires `FluentMigrator 6.*` or later.

## What's New

<details>
<summary><b>v1.7.1</b> — Version alignment (no code changes)</summary>

- SqlServer package aligned to `1.7.0` with zero code changes, per [ADR-0001](docs/adr/0001-synchronized-package-versions.md): both packages now share one version, bumped together on every release. This also refreshes the core DLL embedded in the SqlServer package.
</details>

<details>
<summary><b>v1.7.0</b> — Index & statistics maintenance</summary>

- `ReorganizeIndexes(tableName)` — defragments all indexes on a table (`ALTER INDEX ALL ... REORGANIZE` on SQL Server, `REINDEX TABLE` on PostgreSQL, `OPTIMIZE TABLE` on MySQL, `REINDEX` on SQLite, per-index `REBUILD` loop on Oracle).
- `UpdateStatistics(tableName, samplePercent: 30)` — refreshes optimizer statistics (`UPDATE STATISTICS ... WITH SAMPLE n PERCENT` on SQL Server, `ANALYZE` on PostgreSQL/MySQL/SQLite, `DBMS_STATS.GATHER_TABLE_STATS` on Oracle). Sampling is honored on SQL Server and Oracle only; `null` means server default.
- Both are pure maintenance (no schema changes, empty `Down()`), tables are passed as parameters, and re-running is always safe.
</details>

<details>
<summary><b>v1.6.0</b> — Anonymous object overloads for all data methods</summary>

- All four data methods (`InsertDataIfNotExists`, `UpsertData`, `UpdateDataIfExists`, `DeleteDataIfExists`) now accept anonymous objects instead of dictionaries — `new { code = "ACTIVE" }` instead of `new Dictionary<string, object?> { ["code"] = "ACTIVE" }`.
- Dictionary overloads remain for backwards compatibility and dynamic scenarios.
</details>

<details>
<summary><b>v1.5.1</b> — New methods: upsert, conditional SQL, alter sequence, create-or-replace view</summary>

- `UpsertData` — insert-or-update via provider-specific syntax (`MERGE` on SQL Server/Oracle, `ON CONFLICT` on PostgreSQL/SQLite, `ON DUPLICATE KEY` on MySQL).
- `AlterSequenceIfExists` — alter a sequence if it exists, via raw `ALTER SEQUENCE` clause (SQL Server, PostgreSQL, Oracle).
- `CreateOrReplaceView` — drop + create view in a single call, for updating view definitions between releases.
- `ExecuteSqlIfExists` / `ExecuteSqlIfNotExists` — generic conditional SQL execution escape hatch (SQL Server, PostgreSQL, Oracle).
- README redesign: centered badges, collapsible API Reference and changelog, expanded compatibility table.
</details>

<details>
<summary><b>v1.5.0</b> — Oracle support</summary>

- Added Oracle as the fifth supported provider with Oracle-specific SQL branches where needed (`ALTER TABLE ... MODIFY ... DEFAULT`, PL/SQL exception-swallow for `DROP ... IF EXISTS`, `ALTER INDEX`/`RENAME CONSTRAINT`, `FROM DUAL` for `InsertDataIfNotExists`).
- Added Oracle to the Testcontainers integration suite (`gvenzl/oracle-free`).
- Documented `ORA-01451` limitation for `AlterColumnIfExists`.
</details>

<details>
<summary><b>v1.4.0</b> — Views, triggers, functions, check constraints, data operations</summary>

- `CreateCheckConstraintIfNotExists`, `AddColumnDefaultIfExists` / `DropColumnDefaultIfExists`.
- `CreateViewIfNotExists` / `DropViewIfExists` — idempotent views across all providers.
- `CreateTriggerIfNotExists` / `DropTriggerIfExists` — idempotent triggers.
- `CreateFunctionIfNotExists` / `DropFunctionIfExists` — idempotent SQL functions (SQL Server, PostgreSQL).
- `RenameIndexIfExists`, `RenameConstraintIfExists`.
- `UpdateDataIfExists` / `DeleteDataIfExists`.
</details>

<details>
<summary><b>v1.3.1</b> — MySQL PK fix, Testcontainers integration suite</summary>

- Fixed `CreatePrimaryKeyIfNotExists` on MySQL: the check now correctly handles MySQL's `PRIMARY` naming convention.
- Added Testcontainers-based integration test suite against real SQL Server, PostgreSQL, and MySQL containers.
</details>

<details>
<summary><b>v1.3.0</b> — Foreign keys, primary keys, sequences, seed data</summary>

- `CreateForeignKeyIfNotExists` / `DropForeignKeyIfExists`.
- `CreatePrimaryKeyIfNotExists`, `AlterColumnIfExists`, `RenameTableIfExists`.
- `CreateSequenceIfNotExists` / `DropSequenceIfExists` (raised FluentMigrator dependency to `6.*`).
- `DropColumnDefaultIfExists` (cross-provider), `InsertDataIfNotExists`.
</details>

## Contributing

Contributions are welcome! Please open an issue or pull request on [GitHub](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions).

## License

[MIT](LICENSE)
