using FluentMigrator;
using FluentMigrator.IdempotentExtensions;

namespace FluentMigrator.IdempotentExtensions.Tests.Integration;

// Shared across SQL Server / PostgreSQL / MySQL test runs. schemaName is always omitted (null)
// so every provider exercises its own auto-detected default schema (dbo / public / "").

[Migration(1)]
internal sealed class CreateTableMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists("test_users", t => t
            .WithIdColumn()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("email").AsString(500).Nullable());
    }

    public override void Down() => Delete.Table("test_users");
}

[Migration(2)]
internal sealed class AddColumnMigration : Migration
{
    public override void Up()
    {
        this.CreateColumnIfNotExists("test_users", "age", c => c.AsInt32().Nullable());
    }

    public override void Down() { }
}

[Migration(3)]
internal sealed class AddIndexMigration : Migration
{
    public override void Up()
    {
        this.CreateIndexIfNotExists("test_users", "email", idx => idx.Ascending());
    }

    public override void Down() { }
}

[Migration(4)]
internal sealed class AddCompositeIndexMigration : Migration
{
    public override void Up()
    {
        this.CreateCompositeIndexIfNotExists("test_users", new[] { "name", "age" },
            idx => idx.WithOptions().NonClustered());
    }

    public override void Down() { }
}

[Migration(5)]
internal sealed class CreateLogTableMigration : Migration
{
    public override void Up() => this.CreateLogTableIfNotExists("test_users");
    public override void Down() { }
}

[Migration(6)]
internal sealed class DeleteColumnMigration : Migration
{
    public override void Up() => this.DeleteColumnIfExists("test_users", "age");
    public override void Down() { }
}

[Migration(7)]
internal sealed class DropConstraintMigration : Migration
{
    public override void Up() => this.DropConstraintIfExists("test_users", "nonexistent_constraint");
    public override void Down() { }
}

[Migration(8)]
internal sealed class DropTableMigration : Migration
{
    public override void Up() => this.DropTableIfExists("test_users");
    public override void Down() { }
}

[Migration(9)]
internal sealed class RenameNonExistentColumnMigration : Migration
{
    public override void Up() => this.RenameColumnIfExists("test_users", "nonexistent_col", "new_col");
    public override void Down() { }
}

[Migration(10)]
internal sealed class AddUniqueConstraintMigration : Migration
{
    public override void Up() => this.CreateUniqueConstraintIfNotExists("test_users", "uc_users_email", new[] { "email" });
    public override void Down() { }
}

[Migration(11)]
internal sealed class RenameTableMigration : Migration
{
    public override void Up() => this.RenameTableIfExists("test_users", "test_users_renamed");
    public override void Down() { }
}

[Migration(12)]
internal sealed class RenameNonExistentTableMigration : Migration
{
    public override void Up() => this.RenameTableIfExists("nonexistent_table", "whatever");
    public override void Down() { }
}

[Migration(13)]
internal sealed class InsertSeedDataMigration : Migration
{
    public override void Up()
    {
        this.InsertDataIfNotExists("test_users",
            new Dictionary<string, object?> { ["name"] = "seed-user" },
            new Dictionary<string, object?> { ["email"] = "seed@example.com" });
    }

    public override void Down() { }
}

[Migration(14)]
internal sealed class InsertSeedDataWithNullKeyMigration : Migration
{
    public override void Up()
    {
        this.InsertDataIfNotExists("test_users",
            new Dictionary<string, object?> { ["name"] = "seed-user-nullkey", ["email"] = null });
    }

    public override void Down() { }
}

// ---- provider-only DDL: not supported on SQLite, so only exercised here ----

[Migration(15)]
internal sealed class AlterColumnMigration : Migration
{
    public override void Up()
    {
        this.AlterColumnIfExists("test_users", "email", c => c.AsString(1000).Nullable());
    }

    public override void Down() { }
}

[Migration(16)]
internal sealed class AlterMissingColumnMigration : Migration
{
    public override void Up()
    {
        this.AlterColumnIfExists("test_users", "nonexistent_col", c => c.AsInt32().Nullable());
    }

    public override void Down() { }
}

[Migration(17)]
internal sealed class CreatePrimaryTableForFkMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists("test_roles", t => t
            .WithIdColumn()
            .WithColumn("title").AsString(100).NotNullable());
    }

    public override void Down() { }
}

[Migration(18)]
internal sealed class AddForeignKeyColumnMigration : Migration
{
    public override void Up()
    {
        this.CreateColumnIfNotExists("test_users", "role_id", c => c.AsInt32().Nullable());
    }

    public override void Down() { }
}

[Migration(19)]
internal sealed class AddForeignKeyMigration : Migration
{
    public override void Up()
    {
        this.CreateForeignKeyIfNotExists("test_users", "fk_users_role",
            new[] { "role_id" }, "test_roles", new[] { "id" });
    }

    public override void Down() { }
}

[Migration(20)]
internal sealed class DropForeignKeyMigration : Migration
{
    public override void Up() => this.DropForeignKeyIfExists("test_users", "fk_users_role");
    public override void Down() { }
}

[Migration(21)]
internal sealed class AddPrimaryKeyTableMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists("test_no_pk", t => t
            .WithColumn("code").AsInt32().NotNullable());
    }

    public override void Down() { }
}

[Migration(22)]
internal sealed class CreatePrimaryKeyMigration : Migration
{
    public override void Up() => this.CreatePrimaryKeyIfNotExists("test_no_pk", "pk_test_no_pk", new[] { "code" });
    public override void Down() { }
}

[Migration(23)]
internal sealed class CreateSequenceMigration : Migration
{
    public override void Up() => this.CreateSequenceIfNotExists("test_seq");
    public override void Down() { }
}

[Migration(24)]
internal sealed class DropSequenceMigration : Migration
{
    public override void Up() => this.DropSequenceIfExists("test_seq");
    public override void Down() { }
}

[Migration(25)]
internal sealed class AddDefaultColumnMigration : Migration
{
    public override void Up()
    {
        this.CreateColumnIfNotExists("test_users", "status", c => c.AsString(50).NotNullable().WithDefaultValue("active"));
    }

    public override void Down() { }
}

[Migration(26)]
internal sealed class DropColumnDefaultMigration : Migration
{
    public override void Up() => this.DropColumnDefaultIfExists("test_users", "status");
    public override void Down() { }
}

[Migration(27)]
internal sealed class CreateCheckConstraintMigration : Migration
{
    public override void Up() => this.CreateCheckConstraintIfNotExists("test_users", "ck_users_age", "age >= 0");
    public override void Down() { }
}

[Migration(28)]
internal sealed class AddColumnDefaultMigration : Migration
{
    public override void Up() => this.AddColumnDefaultIfExists("test_users", "age", 18);
    public override void Down() { }
}

[Migration(29)]
internal sealed class CreateViewMigration : Migration
{
    public override void Up() => this.CreateViewIfNotExists("test_users_view", "SELECT id, name FROM test_users");
    public override void Down() { }
}

[Migration(30)]
internal sealed class DropViewMigration : Migration
{
    public override void Up() => this.DropViewIfExists("test_users_view");
    public override void Down() { }
}

// Trigger bodies are inherently provider-specific, so each provider test class supplies its own
// CREATE TRIGGER statement and runs it through this parameterized migration (bypasses ScanIn's
// attribute-based discovery, which is fine — these tests never call MigrateUp/MigrateToLatest,
// only ad-hoc Up(instance) calls on a specific migration object).
internal sealed class CreateTriggerMigration : Migration
{
    private readonly string _triggerName;
    private readonly string _tableName;
    private readonly string _createTriggerSql;

    public CreateTriggerMigration(string triggerName, string tableName, string createTriggerSql)
    {
        _triggerName = triggerName;
        _tableName = tableName;
        _createTriggerSql = createTriggerSql;
    }

    public override void Up() => this.CreateTriggerIfNotExists(_triggerName, _tableName, _createTriggerSql);
    public override void Down() { }
}

internal sealed class DropTriggerMigration : Migration
{
    private readonly string _triggerName;
    private readonly string _tableName;

    public DropTriggerMigration(string triggerName, string tableName)
    {
        _triggerName = triggerName;
        _tableName = tableName;
    }

    public override void Up() => this.DropTriggerIfExists(_triggerName, _tableName);
    public override void Down() { }
}

// PostgreSQL trigger functions must exist before a trigger can reference them.
[Migration(31)]
internal sealed class CreatePostgresTriggerFunctionMigration : Migration
{
    public override void Up()
    {
        this.CreateFunctionIfNotExists("test_trigger_noop_fn", @"
CREATE FUNCTION test_trigger_noop_fn() RETURNS TRIGGER AS $$
BEGIN
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;");
    }

    public override void Down() { }
}

internal sealed class CreateFunctionMigration : Migration
{
    private readonly string _functionName;
    private readonly string _createFunctionSql;

    public CreateFunctionMigration(string functionName, string createFunctionSql)
    {
        _functionName = functionName;
        _createFunctionSql = createFunctionSql;
    }

    public override void Up() => this.CreateFunctionIfNotExists(_functionName, _createFunctionSql);
    public override void Down() { }
}

internal sealed class DropFunctionMigration : Migration
{
    private readonly string _functionName;

    public DropFunctionMigration(string functionName) => _functionName = functionName;

    public override void Up() => this.DropFunctionIfExists(_functionName);
    public override void Down() { }
}

[Migration(32)]
internal sealed class RenameIndexMigration : Migration
{
    public override void Up() => this.RenameIndexIfExists("test_users", "index_email", "idx_users_email_renamed");
    public override void Down() { }
}

[Migration(33)]
internal sealed class RenameConstraintMigration : Migration
{
    public override void Up() => this.RenameConstraintIfExists("test_users", "uc_users_email", "uc_users_email_renamed");
    public override void Down() { }
}

[Migration(34)]
internal sealed class UpdateSeedDataMigration : Migration
{
    public override void Up()
    {
        this.UpdateDataIfExists("test_users",
            new Dictionary<string, object?> { ["name"] = "seed-user" },
            new Dictionary<string, object?> { ["email"] = "updated@example.com" });
    }

    public override void Down() { }
}

[Migration(35)]
internal sealed class DeleteSeedDataMigration : Migration
{
    public override void Up() => this.DeleteDataIfExists("test_users", new Dictionary<string, object?> { ["name"] = "seed-user" });
    public override void Down() { }
}
