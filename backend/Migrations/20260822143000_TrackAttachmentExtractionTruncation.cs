using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260822143000_TrackAttachmentExtractionTruncation")]
public partial class TrackAttachmentExtractionTruncation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ExtractedCharacters",
            table: "article_attachments",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "ExtractionCharacterLimit",
            table: "article_attachments",
            type: "integer",
            nullable: false,
            defaultValue: 50000);

        migrationBuilder.AddColumn<bool>(
            name: "ExtractionTruncated",
            table: "article_attachments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            UPDATE article_attachments
            SET "ExtractedCharacters" = length(COALESCE("ExtractedText", '')),
                "ExtractionTruncated" = length(COALESCE("ExtractedText", '')) >= 50000
            WHERE "ExtractedAt" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ExtractedCharacters", table: "article_attachments");
        migrationBuilder.DropColumn(name: "ExtractionCharacterLimit", table: "article_attachments");
        migrationBuilder.DropColumn(name: "ExtractionTruncated", table: "article_attachments");
    }
}
