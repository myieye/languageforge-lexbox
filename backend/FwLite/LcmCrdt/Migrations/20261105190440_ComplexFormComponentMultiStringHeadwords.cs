using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LcmCrdt.Migrations
{
    /// <inheritdoc />
    public partial class ComplexFormComponentMultiStringHeadwords : Migration
    {
        internal static readonly string[] HeadwordColumns = ["ComplexFormHeadword", "ComponentHeadword"];

        // Headwords used to be plain text with no WS label (Entry.Headword() took the alphabetically first WS), so they go under "default"
        internal static string UpSql(string column) => $$"""
            UPDATE ComplexFormComponents SET {{column}} = CASE
                WHEN {{column}} IS NULL OR {{column}} = '' OR {{column}} = '(Unknown)' THEN '{}'
                WHEN CASE WHEN json_valid({{column}}) THEN json_type({{column}}) END = 'object' THEN {{column}}
                ELSE json_object('default', {{column}})
            END
            """;

        internal static string DownSql(string column) => $$"""
            UPDATE ComplexFormComponents SET {{column}} = CASE
                WHEN CASE WHEN json_valid({{column}}) THEN json_type({{column}}) END = 'object' THEN
                    coalesce(
                        {{column}} ->> 'default',
                        (SELECT value FROM json_each({{column}}) LIMIT 1))
                ELSE {{column}}
            END
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComplexFormHomographNumber",
                table: "ComplexFormComponents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ComponentHomographNumber",
                table: "ComplexFormComponents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            foreach (var column in HeadwordColumns) migrationBuilder.Sql(UpSql(column));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var column in HeadwordColumns) migrationBuilder.Sql(DownSql(column));

            migrationBuilder.DropColumn(
                name: "ComplexFormHomographNumber",
                table: "ComplexFormComponents");

            migrationBuilder.DropColumn(
                name: "ComponentHomographNumber",
                table: "ComplexFormComponents");
        }
    }
}
