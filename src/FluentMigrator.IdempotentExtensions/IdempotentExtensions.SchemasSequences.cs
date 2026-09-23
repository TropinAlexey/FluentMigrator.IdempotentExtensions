namespace FluentMigrator.IdempotentExtensions;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentMigrator.Builders.Alter.Table;
using FluentMigrator.Builders.Create.Index;
using FluentMigrator.Builders.Create.Sequence;
using FluentMigrator.Builders.Create.Table;
using FluentMigrator.Builders.Delete.Constraint;
using FluentMigrator.Builders.Delete.Index;
using FluentMigrator.Infrastructure;

/// <summary>
/// Schemas and sequences.
/// </summary>
public static partial class IdempotentExtensions
{
    public static void CreateSchemaIfNotExists(this Migration self, string schemaName)
    {
        if (!self.Schema.Schema(schemaName).Exists())
            self.Create.Schema(schemaName);
    }

    /// <summary>
    /// Drops <paramref name="schemaName"/> if it exists.
    /// Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="schemaName">Schema to drop.</param>
    public static void DropSchemaIfExists(this Migration self, string schemaName)
    {
        if (self.Schema.Schema(schemaName).Exists())
            self.Delete.Schema(schemaName);
    }

    /// <summary>
    /// Creates <paramref name="sequenceName"/> if it does not already exist.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to create.</param>
    /// <param name="configureSequence">Optional callback to configure increment, min/max, start value, caching, cycling.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void CreateSequenceIfNotExists(
        this Migration self,
        string sequenceName,
        Action<ICreateSequenceSyntax>? configureSequence = null,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            return;

        var sequence = self.Create.Sequence(sequenceName).InSchema(schemaName);
        configureSequence?.Invoke(sequence);
    }

    /// <summary>
    /// Drops <paramref name="sequenceName"/> if it exists.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to drop.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void DropSequenceIfExists(
        this Migration self,
        string sequenceName,
        string? schemaName = null)
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            self.Delete.Sequence(sequenceName).InSchema(schemaName);
    }

    /// <summary>
    /// Adds a named CHECK constraint on <paramref name="tableName"/> if it does not already exist.
    /// Not supported on SQLite (its <c>ALTER TABLE</c> cannot add constraints to an existing table).
    /// </summary>
    /// <remarks>
    /// To drop a check constraint, reuse <see cref="DropConstraintIfExists"/> — it issues a generic
    /// <c>DROP CONSTRAINT</c>, which SQL Server, PostgreSQL, and MySQL (8.0.19+) all accept for CHECK constraints.
    /// The <paramref name="checkSql"/> expression is executed verbatim — trusted developer input only,
    /// never end-user input.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="tableName">Target table name.</param>
    /// <param name="constraintName">Name of the CHECK constraint to create.</param>
    /// <param name="checkSql">The boolean SQL expression to check, without the surrounding parentheses
    /// (e.g. <c>"age &gt;= 0"</c>).</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
    public static void AlterSequenceIfExists(
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
    {
        schemaName ??= self.ResolveDefaultSchema();

        if (!self.Schema.Schema(schemaName).Sequence(sequenceName).Exists())
            return;

        var provider = self.GetProvider();
        var isOracle = provider == DatabaseProvider.Oracle;

        if (isOracle && restartWith.HasValue)
            throw new ArgumentException(
                "restartWith is not supported on Oracle — pass startWith instead (it also restarts the sequence there).",
                nameof(restartWith));

        var clauses = new List<string>();
        if (incrementBy.HasValue) clauses.Add($"INCREMENT BY {incrementBy.Value}");
        if (minValue.HasValue) clauses.Add($"MINVALUE {minValue.Value}");
        if (maxValue.HasValue) clauses.Add($"MAXVALUE {maxValue.Value}");
        if (startWith.HasValue) clauses.Add($"START WITH {startWith.Value}");
        if (restartWith.HasValue) clauses.Add($"RESTART WITH {restartWith.Value}");
        if (cache.HasValue)
            clauses.Add(cache.Value > 0
                ? $"CACHE {cache.Value}"
                : isOracle ? "NOCACHE" : "NO CACHE");
        if (cycle.HasValue)
            clauses.Add(cycle.Value
                ? "CYCLE"
                : isOracle ? "NOCYCLE" : "NO CYCLE");

        if (clauses.Count == 0)
            return;

        // No trailing semicolon on Oracle direct DDL (ORA-00911).
        var terminator = isOracle ? "" : ";";
        self.Execute.Sql($"ALTER SEQUENCE {QualifyTable(provider, schemaName, sequenceName)} {string.Join(" ", clauses)}{terminator}");
    }

    /// <summary>
    /// Drops and recreates <paramref name="viewName"/> in a single call — equivalent to
    /// <see cref="DropViewIfExists"/> followed by <see cref="CreateViewIfNotExists"/>.
    /// If the view does not exist, it is simply created.
    /// </summary>
    /// <remarks>
    /// On SQL Server and SQLite the replace is a DROP followed by CREATE, which discards
    /// permissions granted on the view — re-apply them afterwards if needed. On PostgreSQL
    /// and MySQL (<c>CREATE OR REPLACE</c>) existing grants are preserved.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="viewName">Name of the view to create or replace.</param>
    /// <param name="selectSql">The view's <c>SELECT</c> statement, without the <c>CREATE VIEW ... AS</c> prefix.</param>
    /// <param name="schemaName">Database schema. If <c>null</c>, auto-detected from the database provider.</param>
}
