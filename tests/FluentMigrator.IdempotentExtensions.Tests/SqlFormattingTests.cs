using System;
using System.Reflection;

namespace FluentMigrator.IdempotentExtensions.Tests;

/// <summary>
/// Unit tests for the internal SQL literal/identifier formatting. The helpers are private
/// (they are an implementation detail, not a supported contract), so these tests bind to them
/// via reflection by name — they guard the escaping rules, not the method visibility.
/// </summary>
public sealed class SqlFormattingTests
{
    private static readonly Type ExtensionsType = typeof(IdempotentExtensions);
    private static readonly Type ProviderType =
        ExtensionsType.GetNestedType("DatabaseProvider", BindingFlags.NonPublic)!;

    private static object Provider(string name) => Enum.Parse(ProviderType, name);

    private static string Format(object? value, string provider) =>
        (string)ExtensionsType
            .GetMethod("FormatSqlValue", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new[] { value, Provider(provider) })!;

    private static string Quote(string name, string provider) =>
        (string)ExtensionsType
            .GetMethod("QuoteIdent", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new[] { Provider(provider), name })!;

    private static string Parse(string databaseType) =>
        ExtensionsType
            .GetMethod("ParseDatabaseProvider", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { databaseType })!.ToString()!;

    private static TException InvokeFormat<TException>(object? value, string provider)
        where TException : Exception
    {
        // MethodInfo.Invoke wraps the real exception in TargetInvocationException.
        var ex = Record.Exception(() => Format(value, provider));
        var unwrapped = (ex as TargetInvocationException)?.InnerException ?? ex;
        return Assert.IsType<TException>(unwrapped);
    }

    [Theory]
    [InlineData("SqlServer", "SqlServer")]
    [InlineData("Postgres", "Postgres")]
    [InlineData("MySql", "MySql")]
    [InlineData("MariaDb", "MariaDb")]
    [InlineData("SQLite", "SQLite")]
    [InlineData("Oracle", "Oracle")]
    [InlineData("Snowflake", "Unknown")]
    public void ParseDatabaseProvider_MapsKnownStrings(string input, string expected) =>
        Assert.Equal(expected, Parse(input));

    [Fact]
    public void FormatSqlValue_EscapesSingleQuotesEverywhere()
    {
        Assert.Equal("'o''clock'", Format("o'clock", "SqlServer"));
        Assert.Equal("'o''clock'", Format("o'clock", "Postgres"));
        Assert.Equal("'o''clock'", Format("o'clock", "MySql"));
        Assert.Equal("'o''clock'", Format("o'clock", "SQLite"));
        Assert.Equal("'o''clock'", Format("o'clock", "Oracle"));
    }

    [Fact]
    public void FormatSqlValue_MySqlDoublesBackslashes()
    {
        // MySQL treats backslash as an escape character inside string literals, so a value
        // like \' must have the backslash doubled — otherwise it breaks out of the literal.
        Assert.Equal(@"'x\\y''z'", Format("x\\y'z", "MySql"));
        Assert.Equal(@"'x\\y''z'", Format("x\\y'z", "MariaDb"));
    }

    [Fact]
    public void FormatSqlValue_OtherProvidersLeaveBackslashesAlone()
    {
        Assert.Equal(@"'x\y''z'", Format("x\\y'z", "SqlServer"));
        Assert.Equal(@"'x\y''z'", Format("x\\y'z", "Postgres"));
        Assert.Equal(@"'x\y''z'", Format("x\\y'z", "SQLite"));
        Assert.Equal(@"'x\y''z'", Format("x\\y'z", "Oracle"));
    }

    [Fact]
    public void FormatSqlValue_BooleansAreProviderSpecific()
    {
        Assert.Equal("TRUE", Format(true, "Postgres"));
        Assert.Equal("FALSE", Format(false, "Postgres"));
        Assert.Equal("1", Format(true, "SqlServer"));
        Assert.Equal("0", Format(false, "MySql"));
        Assert.Equal("1", Format(true, "SQLite"));
        Assert.Equal("0", Format(false, "Oracle"));
    }

    [Fact]
    public void FormatSqlValue_NonFiniteFloatsThrowImmediately()
    {
        // NaN/Infinity have no SQL literal representation — fail fast instead of emitting
        // invalid SQL that would blow up server-side.
        InvokeFormat<ArgumentException>(float.NaN, "SqlServer");
        InvokeFormat<ArgumentException>(float.PositiveInfinity, "Postgres");
        InvokeFormat<ArgumentException>(double.NaN, "MySql");
        InvokeFormat<ArgumentException>(double.NegativeInfinity, "Oracle");
    }

    [Fact]
    public void FormatSqlValue_FiniteNumericsUseInvariantCulture()
    {
        Assert.Equal("1.5", Format(1.5, "SqlServer"));
        Assert.Equal("42", Format(42, "Postgres"));
    }

    [Fact]
    public void FormatSqlValue_DateOnlyAndTimeOnly()
    {
        Assert.Equal("'2026-09-23'", Format(new DateOnly(2026, 9, 23), "SqlServer"));
        Assert.Equal("'2026-09-23'", Format(new DateOnly(2026, 9, 23), "Postgres"));
        Assert.Equal("'13:45:01'", Format(new TimeOnly(13, 45, 1), "MySql"));
    }

    [Fact]
    public void FormatSqlValue_ByteArraysAreProviderSpecific()
    {
        var bytes = new byte[] { 0xDE, 0xAD };
        Assert.Equal("0xDEAD", Format(bytes, "SqlServer"));
        Assert.Equal("0xDEAD", Format(bytes, "MySql"));
        Assert.Equal("0xDEAD", Format(bytes, "SQLite"));
        Assert.Equal(@"'\xDEAD'", Format(bytes, "Postgres"));
        Assert.Equal("HEXTORAW('DEAD')", Format(bytes, "Oracle"));
    }

    [Fact]
    public void FormatSqlValue_NullGuidEnum()
    {
        Assert.Equal("NULL", Format(null, "SqlServer"));
        var g = new Guid("11111111-2222-3333-4444-555555555555");
        Assert.Equal("'11111111-2222-3333-4444-555555555555'", Format(g, "Postgres"));
        Assert.Equal("1", Format(DayOfWeek.Monday, "SqlServer"));
        Assert.Equal("'c'", Format('c', "SqlServer"));
    }

    [Fact]
    public void FormatSqlValue_UnsupportedTypesThrow()
    {
        InvokeFormat<NotSupportedException>(new TimeSpan(1), "SqlServer");
        InvokeFormat<NotSupportedException>(new Uri("https://example.com"), "Postgres");
        InvokeFormat<NotSupportedException>(new object(), "MySql");
    }

    [Fact]
    public void QuoteIdent_IsProviderSpecific()
    {
        Assert.Equal("[a]]b]", Quote("a]b", "SqlServer"));
        Assert.Equal("`a``b`", Quote("a`b", "MySql"));
        Assert.Equal("\"mytable\"", Quote("MyTable", "Postgres"));
        Assert.Equal("\"MYTABLE\"", Quote("MyTable", "Oracle"));
        Assert.Equal("\"MyTable\"", Quote("MyTable", "SQLite"));
    }
}
