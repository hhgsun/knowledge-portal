using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "lookup_values",
                type: "character varying(50)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "article_lookup_values",
                columns: table => new
                {
                    article_id = table.Column<string>(type: "text", nullable: false),
                    lookup_value_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_lookup_values", x => new { x.article_id, x.lookup_value_id });
                    table.ForeignKey(
                        name: "fk_article_lookup_values_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_lookup_values_lookup_values_lookup_value_id",
                        column: x => x.lookup_value_id,
                        principalTable: "lookup_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lookup_categories",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cardinality = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "single"),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    default_value_id = table.Column<string>(type: "text", nullable: true),
                    rag_behavior = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "filter"),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookup_categories", x => x.id);
                    table.UniqueConstraint("ak_lookup_categories_key", x => x.key);
                    table.ForeignKey(
                        name: "fk_lookup_categories_lookup_values_default_value_id",
                        column: x => x.default_value_id,
                        principalTable: "lookup_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_lookup_values_lookup_value_id",
                table: "article_lookup_values",
                column: "lookup_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_lookup_categories_default_value_id",
                table: "lookup_categories",
                column: "default_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_lookup_categories_key",
                table: "lookup_categories",
                column: "key",
                unique: true);

            // Existing lookup_values rows must have a category principal before the FK is added.
            // Seed the initial compatibility category and mirror every existing article's
            // content_type into the generic assignment table in the same migration.
            migrationBuilder.Sql(
                """
                INSERT INTO lookup_categories
                    (id, key, label, cardinality, is_required, rag_behavior, sort_order, is_active, created_at)
                VALUES
                    ('lookup-content-type', 'content_type', 'Content Type', 'single', TRUE,
                     'filter', 1, TRUE, NOW())
                ON CONFLICT (key) DO NOTHING;

                INSERT INTO lookup_categories
                    (id, key, label, cardinality, is_required, rag_behavior, sort_order, is_active, created_at)
                SELECT SUBSTRING(MD5(existing.category) FROM 1 FOR 21), existing.category,
                       INITCAP(REPLACE(existing.category, '_', ' ')), 'multiple', FALSE,
                       'none', 100, TRUE, NOW()
                FROM (SELECT DISTINCT category FROM lookup_values
                      WHERE category <> 'content_type') existing
                ON CONFLICT (key) DO NOTHING;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_lookup_values_lookup_categories_category",
                table: "lookup_values",
                column: "category",
                principalTable: "lookup_categories",
                principalColumn: "key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                INSERT INTO article_lookup_values (article_id, lookup_value_id)
                SELECT article.id, lookup.id
                FROM articles article
                JOIN lookup_values lookup
                  ON lookup.category = 'content_type' AND lookup.value = article.content_type
                ON CONFLICT (article_id, lookup_value_id) DO NOTHING;

                UPDATE lookup_categories category
                SET default_value_id = lookup.id
                FROM lookup_values lookup
                WHERE category.key = 'content_type'
                  AND lookup.category = 'content_type'
                  AND lookup.value = 'reference';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lookup_values_lookup_categories_category",
                table: "lookup_values");

            migrationBuilder.DropTable(
                name: "article_lookup_values");

            migrationBuilder.DropTable(
                name: "lookup_categories");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "lookup_values",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)");
        }
    }
}
