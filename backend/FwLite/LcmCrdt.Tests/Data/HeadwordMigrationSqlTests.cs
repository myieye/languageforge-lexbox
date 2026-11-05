using LcmCrdt.Migrations;
using Microsoft.Data.Sqlite;

namespace LcmCrdt.Tests.Data;

public class HeadwordMigrationSqlTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public HeadwordMigrationSqlTests()
    {
        _connection.Open();
        Execute("CREATE TABLE ComplexFormComponents (Id TEXT, ComplexFormHeadword TEXT, ComponentHeadword TEXT);");
    }

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData("na", """{"default":"na"}""")]
    [InlineData(null, "{}")]
    [InlineData("", "{}")]
    [InlineData("(Unknown)", "{}")]
    [InlineData("""{"en":"bank"}""", """{"en":"bank"}""")]
    [InlineData("\"na\"", """{"default":"\"na\""}""")]
    public void UpSql_RewritesLegacyHeadwords(string? stored, string expected)
    {
        Insert(stored);

        Execute(ComplexFormComponentMultiStringHeadwords.UpSql("ComplexFormHeadword"));

        ReadHeadword().Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"en":"bank","default":"na"}""", "na")]
    [InlineData("""{"en":"bank"}""", "bank")]
    [InlineData("{}", null)]
    public void DownSql_PrefersTheDefaultValue(string stored, string? expected)
    {
        Insert(stored);

        Execute(ComplexFormComponentMultiStringHeadwords.DownSql("ComplexFormHeadword"));

        ReadHeadword().Should().Be(expected);
    }

    private void Insert(string? headword)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO ComplexFormComponents VALUES ('c', $headword, NULL)";
        command.Parameters.AddWithValue("$headword", (object?)headword ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private string? ReadHeadword()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT ComplexFormHeadword FROM ComplexFormComponents";
        return command.ExecuteScalar() as string;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
