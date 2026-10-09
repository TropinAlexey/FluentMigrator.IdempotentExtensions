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
    /// <summary>
    /// Creates <paramref name="schemaName"/> if it does not already exist.
    /// Not supported on SQLite.
    /// </summary>
    /// <param name="self">The migration instance.</param>
    /// <param name="schemaName">Schema to create.</param>
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
    /// Alters <paramref name="sequenceName"/> if it already exists; no-op otherwise.
    /// Not supported on MySQL/MariaDB or SQLite.
    /// </summary>
    /// <remarks>
    /// FluentMigrator has no <c>Alter.Sequence</c> API, so this builds and executes a raw
    /// <c>ALTER SEQUENCE</c> statement from the supplied parameters. At least one parameter
    /// must be non-null. Provider differences are handled internally — e.g. Oracle uses
    /// <c>START WITH</c> instead of <c>RESTART WITH</c>, and <c>NOCYCLE</c>/<c>NOCACHE</c>
    /// instead of <c>NO CYCLE</c>/<c>NO CACHE</c>.
    /// </remarks>
    /// <param name="self">The migration instance.</param>
    /// <param name="sequenceName">Name of the sequence to alter.</param>
    /// <param name="incrementBy">New increment value.</param>
    /// <param name="minValue">New minimum value.</param>
    /// <param name="maxValue">New maximum value.</param>
    /// <param name="restartWith">Restart the sequence at this value. Not supported on Oracle (use <paramref name="startWith"/> instead).</param>
    /// <param name="startWith">Set the start value. On Oracle, also restarts the sequence.</param>
    /// <param name="cache">Number of values to cache. Pass <c>0</c> to disable caching.</param>
    /// <param name="cycle">Whether the sequence should cycle when it reaches its limit.</param>
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
}
