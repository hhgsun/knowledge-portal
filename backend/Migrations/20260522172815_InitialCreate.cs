using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    avatar = table.Column<string>(type: "TEXT", nullable: true),
                    role = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "viewer"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    key_hash = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    permissions = table.Column<string>(type: "TEXT", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: true),
                    excerpt = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft"),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "reference"),
                    difficulty = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "beginner"),
                    audience = table.Column<string>(type: "TEXT", nullable: true),
                    created_via_api_key_id = table.Column<string>(type: "TEXT", nullable: true),
                    read_time_minutes = table.Column<int>(type: "INTEGER", nullable: true),
                    published_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_reviewed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    review_interval_days = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 90),
                    indexed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_articles", x => x.id);
                    table.ForeignKey(
                        name: "FK_articles_api_keys_created_via_api_key_id",
                        column: x => x.created_via_api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_articles_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_feedback",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    article_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: true),
                    helpful = table.Column<bool>(type: "INTEGER", nullable: false),
                    comment = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_article_feedback_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_feedback_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "article_tags",
                columns: table => new
                {
                    article_id = table.Column<string>(type: "TEXT", nullable: false),
                    tag_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_tags", x => new { x.article_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_article_tags_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    article_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: true),
                    changed_by = table.Column<string>(type: "TEXT", nullable: false),
                    change_summary = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_article_versions_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_versions_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_views",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    article_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_views", x => x.id);
                    table.ForeignKey(
                        name: "FK_article_views_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_article_views_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "search_queries",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    query = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: true),
                    results_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    clicked_article_id = table.Column<string>(type: "TEXT", nullable: true),
                    search_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "fulltext"),
                    response_time_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_queries", x => x.id);
                    table.ForeignKey(
                        name: "FK_search_queries_articles_clicked_article_id",
                        column: x => x.clicked_article_id,
                        principalTable: "articles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_search_queries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_user_id",
                table: "api_keys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_feedback_article_id",
                table: "article_feedback",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_feedback_user_id",
                table: "article_feedback",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_tags_tag_id",
                table: "article_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_versions_article_id",
                table: "article_versions",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_versions_changed_by",
                table: "article_versions",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "IX_article_views_article_id",
                table: "article_views",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "IX_article_views_user_id",
                table: "article_views",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_articles_created_via_api_key_id",
                table: "articles",
                column: "created_via_api_key_id");

            migrationBuilder.CreateIndex(
                name: "IX_articles_owner_id",
                table: "articles",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_articles_slug",
                table: "articles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_queries_clicked_article_id",
                table: "search_queries",
                column: "clicked_article_id");

            migrationBuilder.CreateIndex(
                name: "IX_search_queries_user_id",
                table: "search_queries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_feedback");

            migrationBuilder.DropTable(
                name: "article_tags");

            migrationBuilder.DropTable(
                name: "article_versions");

            migrationBuilder.DropTable(
                name: "article_views");

            migrationBuilder.DropTable(
                name: "search_queries");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
