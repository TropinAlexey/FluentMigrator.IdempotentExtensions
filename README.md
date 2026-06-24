# FluentMigrator.IdempotentExtensions

Idempotent extension methods for [FluentMigrator](https://fluentmigrator.github.io/) migrations — safe to run multiple times on any database.

[![NuGet](https://img.shields.io/nuget/v/FluentMigrator.IdempotentExtensions.svg)](https://www.nuget.org/packages/FluentMigrator.IdempotentExtensions/)
[![NuGet (SqlServer)](https://img.shields.io/nuget/v/FluentMigrator.IdempotentExtensions.SqlServer.svg)](https://www.nuget.org/packages/FluentMigrator.IdempotentExtensions.SqlServer/)
[![CI](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml/badge.svg)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml)

## Packages

| Package | Description |
|---------|-------------|
| `FluentMigrator.IdempotentExtensions` | DB-agnostic helpers (SQL Server, PostgreSQL, MySQL, SQLite) |
| `FluentMigrator.IdempotentExtensions.SqlServer` | SQL Server / Azure SQL specific helpers |

## Installation

```shell
dotnet add package FluentMigrator.IdempotentExtensions
```

For SQL Server-specific helpers (e.g. `DropDefaultConstraintIfExists`):

```shell
dotnet add package FluentMigrator.IdempotentExtensions.SqlServer
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

        // Creates audit log table (users_log) if it doesn't exist
        this.CreateLogTableIfNotExists("users");
    }

    public override void Down()
    {
        Delete.Table("users");
        Delete.Table("users_log");
    }
}
```

## API Reference

### Core package (`FluentMigrator.IdempotentExtensions`)

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

#### `DropPrimaryKeyIfExists()`

```csharp
IFluentSyntax? DropPrimaryKeyIfExists(
    this Migration self,
    string tableName,
    string keyName,
    Func<IDeleteConstraintOnTableSyntax, IFluentSyntax> configureDelete,
    string schemaName = "dbo")
```

Drops a primary key or unique constraint by name only if it exists.

---

### SQL Server package (`FluentMigrator.IdempotentExtensions.SqlServer`)

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
| `CreateColumnIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DeleteColumnIfExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateLogTableIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `CreateCompositeIndexIfNotExists` | ✅ | ✅ | ✅ | ✅ |
| `DropIndexIfExists` | ✅ | ✅ | ✅ | ✅ |
| `DropPrimaryKeyIfExists` | ✅ | ✅ | ✅ | ✅ |
| **SqlServer package** | | | | |
| `DropDefaultConstraintIfExists` | ✅ | ❌ | ❌ | ❌ |

> **Note on `schemaName`:** The default value is `"dbo"` (SQL Server convention).
> For PostgreSQL use `"public"`, for MySQL omit the schema or pass the database name.

## License

MIT
