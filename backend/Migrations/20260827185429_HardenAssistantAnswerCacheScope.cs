using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class HardenAssistantAnswerCacheScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assistant_answer_cache_user_scope_query_fingerprint",
                table: "assistant_answer_cache");

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "assistant_answer_cache",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE assistant_answer_cache
                SET user_id = split_part(user_scope, '|', 1);
                DELETE FROM assistant_answer_cache c
                WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = c.user_id);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "assistant_answer_cache",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_answer_cache_user_id",
                table: "assistant_answer_cache",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_answer_cache_user_scope_query_fingerprint_corpus_~",
                table: "assistant_answer_cache",
                columns: new[] { "user_scope", "query_fingerprint", "corpus_fingerprint", "runtime_fingerprint" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_answer_cache_users_user_id",
                table: "assistant_answer_cache",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assistant_answer_cache_users_user_id",
                table: "assistant_answer_cache");

            migrationBuilder.DropIndex(
                name: "ix_assistant_answer_cache_user_id",
                table: "assistant_answer_cache");

            migrationBuilder.DropIndex(
                name: "ix_assistant_answer_cache_user_scope_query_fingerprint_corpus_~",
                table: "assistant_answer_cache");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "assistant_answer_cache");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_answer_cache_user_scope_query_fingerprint",
                table: "assistant_answer_cache",
                columns: new[] { "user_scope", "query_fingerprint" });
        }
    }
}
