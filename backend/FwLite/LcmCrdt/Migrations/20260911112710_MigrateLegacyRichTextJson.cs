using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LcmCrdt.Migrations
{
    /// <summary>
    /// Rewrites rich text columns in the projected tables from their pre-rich-text shapes to the current ones, so SQL
    /// queries only ever see one shape:
    /// a RichMultiString value that is a plain string becomes a single span, an ExampleSentence.Translations object
    /// becomes a list of Translation, and an ExampleSentence.Reference that is raw text becomes a RichString.
    /// Values that C# drops when reading (empty strings, spanless rich strings) are dropped here too.
    /// </summary>
    public partial class MigrateLegacyRichTextJson : Migration
    {
        //assigned to translations that predate translation ids; FwLiteProjectSync's CrdtRepairs replaces it later
        private const string MissingTranslationId = "3dce1982-8e93-44f1-b92c-e9c7bdf72801";

        //sqlite's trim() takes a character set, it has no \s equivalent. C# uses IsNullOrWhiteSpace, which covers more,
        //but these are the characters that turn up in practice.
        private const string Whitespace = "' ' || char(9) || char(10) || char(13)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RewriteRichMultiString("Entry", "LiteralMeaning"));
            migrationBuilder.Sql(RewriteRichMultiString("Entry", "Note"));
            migrationBuilder.Sql(RewriteRichMultiString("Sense", "Definition"));
            migrationBuilder.Sql(RewriteRichMultiString("ExampleSentence", "Sentence"));

            migrationBuilder.Sql(RewriteRichMultiString("ExampleSentence", "Translations"));
            migrationBuilder.Sql($"""
                UPDATE ExampleSentence
                SET Translations = CASE WHEN (SELECT count(*) FROM json_each(ExampleSentence.Translations)) = 0
                        THEN json_array()
                        ELSE json_array(json_object('Id', '{MissingTranslationId}', 'Text', json(Translations)))
                    END
                WHERE json_valid(Translations) AND json_type(Translations) = 'object';
                """);

            migrationBuilder.Sql($"""
                UPDATE ExampleSentence
                SET Reference = CASE
                        WHEN json_valid(Reference) AND json_type(Reference) = 'object' THEN NULL
                        WHEN trim(Reference, {Whitespace}) = '' THEN NULL
                        ELSE json_object('Spans', json_array(json_object('Text', Reference, 'Ws', 'default')))
                    END
                WHERE Reference IS NOT NULL
                  AND CASE WHEN json_valid(Reference) AND json_type(Reference) = 'object'
                           THEN json_array_length(coalesce(json_extract(Reference, '$.Spans'), json_array())) = 0
                           ELSE 1 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //no-op: the shapes this rewrites are legacy, nothing reads them any more
        }

        /// <summary>
        /// Rewrites every plain string value in a RichMultiString column to a single span, and drops the keys C# drops
        /// when reading: empty strings and rich strings without spans. Rows already in the current shape aren't touched.
        /// </summary>
        private static string RewriteRichMultiString(string table, string column)
        {
            return $"""
                UPDATE {table}
                SET {column} = (
                    SELECT coalesce(json_group_object(kv.key, json(CASE kv.type
                                WHEN 'text' THEN json_object('Spans', json_array(json_object('Text', kv.value, 'Ws', kv.key)))
                                ELSE kv.value
                            END)), json_object())
                    FROM json_each({table}.{column}) kv
                    WHERE CASE kv.type
                              WHEN 'text' THEN trim(kv.value, {Whitespace}) <> ''
                              WHEN 'object' THEN json_array_length(coalesce(json_extract(kv.value, '$.Spans'), json_array())) > 0
                              ELSE 0
                          END)
                WHERE json_valid({column}) AND json_type({column}) = 'object'
                  AND EXISTS (
                      SELECT 1 FROM json_each({table}.{column}) kv
                      WHERE kv.type <> 'object'
                         OR json_array_length(coalesce(json_extract(kv.value, '$.Spans'), json_array())) = 0);
                """;
        }
    }
}
