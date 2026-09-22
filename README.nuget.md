# FluentMigrator.IdempotentExtensions

> Idempotent extension methods for [FluentMigrator](https://fluentmigrator.github.io/) — safe to run multiple times on any database.

[![NuGet](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/)
[![NuGet downloads](https://img.shields.io/nuget/dt/TropinAlexey.FluentMigrator.IdempotentExtensions?style=flat-square&logo=nuget&label=Downloads)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/)
[![NuGet SqlServer](https://img.shields.io/nuget/v/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer?style=flat-square&logo=nuget&label=SqlServer)](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/)
[![CI](https://img.shields.io/github/actions/workflow/status/TropinAlexey/FluentMigrator.IdempotentExtensions/ci.yml?style=flat-square&logo=github&label=CI)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/TropinAlexey/FluentMigrator.IdempotentExtensions?style=flat-square)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/blob/main/LICENSE)
[![.NET Standard 2.0](https://img.shields.io/badge/.NET%20Standard-2.0-512bd4?style=flat-square&logo=dotnet)](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions)

## Why

Regular FluentMigrator migrations assume every database starts from the same known state and are applied exactly once, in order. In practice, databases drift: manual hotfixes, partially-applied migrations, or independently evolved instances end up with different schemas.

Idempotent extensions let you write a migration that **checks what already exists** and **only applies what's missing** — so you can run it against any divergent database and converge them all to the same target schema.

## Supported databases

| | SQL Server | PostgreSQL | MySQL | SQLite | Oracle |
|---|:---:|:---:|:---:|:---:|:---:|
| **40+ methods** | Yes | Yes | Yes | Yes | Yes |
| **Testcontainers tests** | Yes | Yes | Yes | — | Yes |

## Packages

| Package | Description |
|---------|-------------|
| [TropinAlexey.FluentMigrator.IdempotentExtensions](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions/) | DB-agnostic helpers (SQL Server, PostgreSQL, MySQL, SQLite, Oracle) |
| [TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer](https://www.nuget.org/packages/TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer/) | SQL Server / Azure SQL specific helpers |

## Installation

```shell
dotnet add package TropinAlexey.FluentMigrator.IdempotentExtensions
```

For SQL Server-specific helpers (e.g. `DropDefaultConstraintIfExists`):

```shell
dotnet add package TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer
```

## Quick start

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

SQL Server-only helpers (package `TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer`):

```csharp
using FluentMigrator.IdempotentExtensions.SqlServer;

this.DropDefaultConstraintIfExists("users", "status"); // finds the auto-named DEFAULT via sys.default_constraints
```

## What's new in 1.7.2

- Security hardening: provider-aware identifier quoting/escaping across all raw SQL; see the GitHub changelog for details.
- Correctness fixes: `CreateTableIfNotExists` honors `schemaName`, Oracle `DropViewIfExists`, PostgreSQL booleans, null-key rejection in `UpsertData`.
- Behavior changes: default index is now `index_{table}_{column}`; key-only upserts are `DO NOTHING` on PostgreSQL/SQLite.

## What's new in 1.7.1

- Version alignment per ADR-0001: both packages share one version.
- This package page now renders from a NuGet-friendly README (pure Markdown, no HTML). Full API reference and changelog live on GitHub.

## Full documentation

The complete API reference, compatibility matrix, use cases, and changelog live on GitHub:

[https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions)

Highlights:

- Tables, columns, indexes, constraints and keys, column defaults, schemas, sequences
- Idempotent data operations: `InsertDataIfNotExists`, `UpsertData`, `UpdateDataIfExists`, `DeleteDataIfExists`
- Views, triggers, functions, conditional SQL (`ExecuteSqlIfExists` / `ExecuteSqlIfNotExists`)
- Maintenance: `ReorganizeIndexes`, `UpdateStatistics`
- `schemaName` is auto-detected per provider (`dbo` on SQL Server, `public` on PostgreSQL); requires `FluentMigrator 6.*` or later

## License

[MIT](https://github.com/TropinAlexey/FluentMigrator.IdempotentExtensions/blob/main/LICENSE)
