using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameToPascalCaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_api_keys_users_user_id",
                table: "api_keys");

            migrationBuilder.DropForeignKey(
                name: "FK_article_feedback_articles_article_id",
                table: "article_feedback");

            migrationBuilder.DropForeignKey(
                name: "FK_article_feedback_users_user_id",
                table: "article_feedback");

            migrationBuilder.DropForeignKey(
                name: "FK_article_tags_articles_article_id",
                table: "article_tags");

            migrationBuilder.DropForeignKey(
                name: "FK_article_tags_tags_tag_id",
                table: "article_tags");

            migrationBuilder.DropForeignKey(
                name: "FK_article_versions_articles_article_id",
                table: "article_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_article_versions_users_changed_by",
                table: "article_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_article_views_articles_article_id",
                table: "article_views");

            migrationBuilder.DropForeignKey(
                name: "FK_article_views_users_user_id",
                table: "article_views");

            migrationBuilder.DropForeignKey(
                name: "FK_articles_api_keys_created_via_api_key_id",
                table: "articles");

            migrationBuilder.DropForeignKey(
                name: "FK_articles_users_owner_id",
                table: "articles");

            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_articles_clicked_article_id",
                table: "search_queries");

            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_users_user_id",
                table: "search_queries");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "users",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "users",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "users",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "avatar",
                table: "users",
                newName: "Avatar");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "users",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                table: "users",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "users",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_users_email",
                table: "users",
                newName: "IX_users_Email");

            migrationBuilder.RenameColumn(
                name: "slug",
                table: "tags",
                newName: "Slug");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "tags",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "tags",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_tags_slug",
                table: "tags",
                newName: "IX_tags_Slug");

            migrationBuilder.RenameColumn(
                name: "query",
                table: "search_queries",
                newName: "Query");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "search_queries",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "search_queries",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "search_type",
                table: "search_queries",
                newName: "SearchType");

            migrationBuilder.RenameColumn(
                name: "results_count",
                table: "search_queries",
                newName: "ResultsCount");

            migrationBuilder.RenameColumn(
                name: "response_time_ms",
                table: "search_queries",
                newName: "ResponseTimeMs");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "search_queries",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "clicked_article_id",
                table: "search_queries",
                newName: "ClickedArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_search_queries_user_id",
                table: "search_queries",
                newName: "IX_search_queries_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_search_queries_clicked_article_id",
                table: "search_queries",
                newName: "IX_search_queries_ClickedArticleId");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "articles",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "articles",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "slug",
                table: "articles",
                newName: "Slug");

            migrationBuilder.RenameColumn(
                name: "excerpt",
                table: "articles",
                newName: "Excerpt");

            migrationBuilder.RenameColumn(
                name: "difficulty",
                table: "articles",
                newName: "Difficulty");

            migrationBuilder.RenameColumn(
                name: "content",
                table: "articles",
                newName: "Content");

            migrationBuilder.RenameColumn(
                name: "audience",
                table: "articles",
                newName: "Audience");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "articles",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "articles",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "review_interval_days",
                table: "articles",
                newName: "ReviewIntervalDays");

            migrationBuilder.RenameColumn(
                name: "read_time_minutes",
                table: "articles",
                newName: "ReadTimeMinutes");

            migrationBuilder.RenameColumn(
                name: "published_at",
                table: "articles",
                newName: "PublishedAt");

            migrationBuilder.RenameColumn(
                name: "owner_id",
                table: "articles",
                newName: "OwnerId");

            migrationBuilder.RenameColumn(
                name: "last_reviewed_at",
                table: "articles",
                newName: "LastReviewedAt");

            migrationBuilder.RenameColumn(
                name: "indexed_at",
                table: "articles",
                newName: "IndexedAt");

            migrationBuilder.RenameColumn(
                name: "created_via_api_key_id",
                table: "articles",
                newName: "CreatedViaApiKeyId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "articles",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "content_type",
                table: "articles",
                newName: "ContentType");

            migrationBuilder.RenameIndex(
                name: "IX_articles_slug",
                table: "articles",
                newName: "IX_articles_Slug");

            migrationBuilder.RenameIndex(
                name: "IX_articles_owner_id",
                table: "articles",
                newName: "IX_articles_OwnerId");

            migrationBuilder.RenameIndex(
                name: "IX_articles_created_via_api_key_id",
                table: "articles",
                newName: "IX_articles_CreatedViaApiKeyId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "article_views",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "article_views",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "article_views",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "article_id",
                table: "article_views",
                newName: "ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_article_views_user_id",
                table: "article_views",
                newName: "IX_article_views_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_article_views_article_id",
                table: "article_views",
                newName: "IX_article_views_ArticleId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "article_versions",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "article_versions",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "content",
                table: "article_versions",
                newName: "Content");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "article_versions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "article_versions",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "changed_by",
                table: "article_versions",
                newName: "ChangedBy");

            migrationBuilder.RenameColumn(
                name: "change_summary",
                table: "article_versions",
                newName: "ChangeSummary");

            migrationBuilder.RenameColumn(
                name: "article_id",
                table: "article_versions",
                newName: "ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_article_versions_changed_by",
                table: "article_versions",
                newName: "IX_article_versions_ChangedBy");

            migrationBuilder.RenameIndex(
                name: "IX_article_versions_article_id",
                table: "article_versions",
                newName: "IX_article_versions_ArticleId");

            migrationBuilder.RenameColumn(
                name: "tag_id",
                table: "article_tags",
                newName: "TagId");

            migrationBuilder.RenameColumn(
                name: "article_id",
                table: "article_tags",
                newName: "ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_article_tags_tag_id",
                table: "article_tags",
                newName: "IX_article_tags_TagId");

            migrationBuilder.RenameColumn(
                name: "helpful",
                table: "article_feedback",
                newName: "Helpful");

            migrationBuilder.RenameColumn(
                name: "comment",
                table: "article_feedback",
                newName: "Comment");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "article_feedback",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "article_feedback",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "article_feedback",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "article_id",
                table: "article_feedback",
                newName: "ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_article_feedback_user_id",
                table: "article_feedback",
                newName: "IX_article_feedback_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_article_feedback_article_id",
                table: "article_feedback",
                newName: "IX_article_feedback_ArticleId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "api_keys",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "api_keys",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "api_keys",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "last_used_at",
                table: "api_keys",
                newName: "LastUsedAt");

            migrationBuilder.RenameColumn(
                name: "key_prefix",
                table: "api_keys",
                newName: "KeyPrefix");

            migrationBuilder.RenameColumn(
                name: "key_hash",
                table: "api_keys",
                newName: "KeyHash");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "api_keys",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "api_keys",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_api_keys_user_id",
                table: "api_keys",
                newName: "IX_api_keys_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_api_keys_key_prefix",
                table: "api_keys",
                newName: "IX_api_keys_KeyPrefix");

            migrationBuilder.AddForeignKey(
                name: "FK_api_keys_users_UserId",
                table: "api_keys",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_feedback_articles_ArticleId",
                table: "article_feedback",
                column: "ArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_feedback_users_UserId",
                table: "article_feedback",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_article_tags_articles_ArticleId",
                table: "article_tags",
                column: "ArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_tags_tags_TagId",
                table: "article_tags",
                column: "TagId",
                principalTable: "tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_versions_articles_ArticleId",
                table: "article_versions",
                column: "ArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_versions_users_ChangedBy",
                table: "article_versions",
                column: "ChangedBy",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_views_articles_ArticleId",
                table: "article_views",
                column: "ArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_views_users_UserId",
                table: "article_views",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_articles_api_keys_CreatedViaApiKeyId",
                table: "articles",
                column: "CreatedViaApiKeyId",
                principalTable: "api_keys",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_articles_users_OwnerId",
                table: "articles",
                column: "OwnerId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries",
                column: "ClickedArticleId",
                principalTable: "articles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_users_UserId",
                table: "search_queries",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_api_keys_users_UserId",
                table: "api_keys");

            migrationBuilder.DropForeignKey(
                name: "FK_article_feedback_articles_ArticleId",
                table: "article_feedback");

            migrationBuilder.DropForeignKey(
                name: "FK_article_feedback_users_UserId",
                table: "article_feedback");

            migrationBuilder.DropForeignKey(
                name: "FK_article_tags_articles_ArticleId",
                table: "article_tags");

            migrationBuilder.DropForeignKey(
                name: "FK_article_tags_tags_TagId",
                table: "article_tags");

            migrationBuilder.DropForeignKey(
                name: "FK_article_versions_articles_ArticleId",
                table: "article_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_article_versions_users_ChangedBy",
                table: "article_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_article_views_articles_ArticleId",
                table: "article_views");

            migrationBuilder.DropForeignKey(
                name: "FK_article_views_users_UserId",
                table: "article_views");

            migrationBuilder.DropForeignKey(
                name: "FK_articles_api_keys_CreatedViaApiKeyId",
                table: "articles");

            migrationBuilder.DropForeignKey(
                name: "FK_articles_users_OwnerId",
                table: "articles");

            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_articles_ClickedArticleId",
                table: "search_queries");

            migrationBuilder.DropForeignKey(
                name: "FK_search_queries_users_UserId",
                table: "search_queries");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "users",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "users",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Avatar",
                table: "users",
                newName: "avatar");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "users",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "users",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_users_Email",
                table: "users",
                newName: "IX_users_email");

            migrationBuilder.RenameColumn(
                name: "Slug",
                table: "tags",
                newName: "slug");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "tags",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "tags",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_tags_Slug",
                table: "tags",
                newName: "IX_tags_slug");

            migrationBuilder.RenameColumn(
                name: "Query",
                table: "search_queries",
                newName: "query");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "search_queries",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "search_queries",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "SearchType",
                table: "search_queries",
                newName: "search_type");

            migrationBuilder.RenameColumn(
                name: "ResultsCount",
                table: "search_queries",
                newName: "results_count");

            migrationBuilder.RenameColumn(
                name: "ResponseTimeMs",
                table: "search_queries",
                newName: "response_time_ms");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "search_queries",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ClickedArticleId",
                table: "search_queries",
                newName: "clicked_article_id");

            migrationBuilder.RenameIndex(
                name: "IX_search_queries_UserId",
                table: "search_queries",
                newName: "IX_search_queries_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_search_queries_ClickedArticleId",
                table: "search_queries",
                newName: "IX_search_queries_clicked_article_id");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "articles",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "articles",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Slug",
                table: "articles",
                newName: "slug");

            migrationBuilder.RenameColumn(
                name: "Excerpt",
                table: "articles",
                newName: "excerpt");

            migrationBuilder.RenameColumn(
                name: "Difficulty",
                table: "articles",
                newName: "difficulty");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "articles",
                newName: "content");

            migrationBuilder.RenameColumn(
                name: "Audience",
                table: "articles",
                newName: "audience");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "articles",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "articles",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ReviewIntervalDays",
                table: "articles",
                newName: "review_interval_days");

            migrationBuilder.RenameColumn(
                name: "ReadTimeMinutes",
                table: "articles",
                newName: "read_time_minutes");

            migrationBuilder.RenameColumn(
                name: "PublishedAt",
                table: "articles",
                newName: "published_at");

            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "articles",
                newName: "owner_id");

            migrationBuilder.RenameColumn(
                name: "LastReviewedAt",
                table: "articles",
                newName: "last_reviewed_at");

            migrationBuilder.RenameColumn(
                name: "IndexedAt",
                table: "articles",
                newName: "indexed_at");

            migrationBuilder.RenameColumn(
                name: "CreatedViaApiKeyId",
                table: "articles",
                newName: "created_via_api_key_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "articles",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ContentType",
                table: "articles",
                newName: "content_type");

            migrationBuilder.RenameIndex(
                name: "IX_articles_Slug",
                table: "articles",
                newName: "IX_articles_slug");

            migrationBuilder.RenameIndex(
                name: "IX_articles_OwnerId",
                table: "articles",
                newName: "IX_articles_owner_id");

            migrationBuilder.RenameIndex(
                name: "IX_articles_CreatedViaApiKeyId",
                table: "articles",
                newName: "IX_articles_created_via_api_key_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "article_views",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "article_views",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "article_views",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ArticleId",
                table: "article_views",
                newName: "article_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_views_UserId",
                table: "article_views",
                newName: "IX_article_views_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_views_ArticleId",
                table: "article_views",
                newName: "IX_article_views_article_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "article_versions",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "article_versions",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "article_versions",
                newName: "content");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "article_versions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "article_versions",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ChangedBy",
                table: "article_versions",
                newName: "changed_by");

            migrationBuilder.RenameColumn(
                name: "ChangeSummary",
                table: "article_versions",
                newName: "change_summary");

            migrationBuilder.RenameColumn(
                name: "ArticleId",
                table: "article_versions",
                newName: "article_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_versions_ChangedBy",
                table: "article_versions",
                newName: "IX_article_versions_changed_by");

            migrationBuilder.RenameIndex(
                name: "IX_article_versions_ArticleId",
                table: "article_versions",
                newName: "IX_article_versions_article_id");

            migrationBuilder.RenameColumn(
                name: "TagId",
                table: "article_tags",
                newName: "tag_id");

            migrationBuilder.RenameColumn(
                name: "ArticleId",
                table: "article_tags",
                newName: "article_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_tags_TagId",
                table: "article_tags",
                newName: "IX_article_tags_tag_id");

            migrationBuilder.RenameColumn(
                name: "Helpful",
                table: "article_feedback",
                newName: "helpful");

            migrationBuilder.RenameColumn(
                name: "Comment",
                table: "article_feedback",
                newName: "comment");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "article_feedback",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "article_feedback",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "article_feedback",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ArticleId",
                table: "article_feedback",
                newName: "article_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_feedback_UserId",
                table: "article_feedback",
                newName: "IX_article_feedback_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_article_feedback_ArticleId",
                table: "article_feedback",
                newName: "IX_article_feedback_article_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "api_keys",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "api_keys",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "api_keys",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "LastUsedAt",
                table: "api_keys",
                newName: "last_used_at");

            migrationBuilder.RenameColumn(
                name: "KeyPrefix",
                table: "api_keys",
                newName: "key_prefix");

            migrationBuilder.RenameColumn(
                name: "KeyHash",
                table: "api_keys",
                newName: "key_hash");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "api_keys",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "api_keys",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_api_keys_UserId",
                table: "api_keys",
                newName: "IX_api_keys_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_api_keys_KeyPrefix",
                table: "api_keys",
                newName: "IX_api_keys_key_prefix");

            migrationBuilder.AddForeignKey(
                name: "FK_api_keys_users_user_id",
                table: "api_keys",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_feedback_articles_article_id",
                table: "article_feedback",
                column: "article_id",
                principalTable: "articles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_feedback_users_user_id",
                table: "article_feedback",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_article_tags_articles_article_id",
                table: "article_tags",
                column: "article_id",
                principalTable: "articles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_tags_tags_tag_id",
                table: "article_tags",
                column: "tag_id",
                principalTable: "tags",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_versions_articles_article_id",
                table: "article_versions",
                column: "article_id",
                principalTable: "articles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_versions_users_changed_by",
                table: "article_versions",
                column: "changed_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_views_articles_article_id",
                table: "article_views",
                column: "article_id",
                principalTable: "articles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_article_views_users_user_id",
                table: "article_views",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_articles_api_keys_created_via_api_key_id",
                table: "articles",
                column: "created_via_api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_articles_users_owner_id",
                table: "articles",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_articles_clicked_article_id",
                table: "search_queries",
                column: "clicked_article_id",
                principalTable: "articles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_search_queries_users_user_id",
                table: "search_queries",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");
        }
    }
}
