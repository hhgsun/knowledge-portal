using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleAssistantConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM assistant_conversations;");

            migrationBuilder.DropIndex(
                name: "ix_assistant_conversations_user_id_updated_at",
                table: "assistant_conversations");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_conversations_user_id",
                table: "assistant_conversations",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assistant_conversations_user_id",
                table: "assistant_conversations");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_conversations_user_id_updated_at",
                table: "assistant_conversations",
                columns: new[] { "user_id", "updated_at" });
        }
    }
}
