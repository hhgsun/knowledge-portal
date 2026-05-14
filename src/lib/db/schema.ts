import { sqliteTable, text, integer, real } from "drizzle-orm/sqlite-core";
import { relations } from "drizzle-orm";

// ─── Users & Auth ───────────────────────────────────────────────────────────

export const users = sqliteTable("users", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  email: text("email").notNull().unique(),
  passwordHash: text("password_hash").notNull(),
  avatar: text("avatar"),
  role: text("role", { enum: ["admin", "editor", "viewer"] })
    .notNull()
    .default("viewer"),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
  updatedAt: integer("updated_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

export const apiKeys = sqliteTable("api_keys", {
  id: text("id").primaryKey(),
  userId: text("user_id")
    .notNull()
    .references(() => users.id, { onDelete: "cascade" }),
  keyHash: text("key_hash").notNull(),
  name: text("name").notNull(),
  permissions: text("permissions", { mode: "json" }).$type<string[]>(),
  lastUsedAt: integer("last_used_at", { mode: "timestamp" }),
  expiresAt: integer("expires_at", { mode: "timestamp" }),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

// ─── Tags ───────────────────────────────────────────────────────────────────

export const tags = sqliteTable("tags", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  slug: text("slug").notNull().unique(),
});

// ─── Articles ───────────────────────────────────────────────────────────────

export const articles = sqliteTable("articles", {
  id: text("id").primaryKey(),
  title: text("title").notNull(),
  slug: text("slug").notNull().unique(),
  content: text("content", { mode: "json" }).$type<Record<string, unknown>>(),
  excerpt: text("excerpt"),
  status: text("status", {
    enum: ["draft", "in_review", "published", "archived"],
  })
    .notNull()
    .default("draft"),
  ownerId: text("owner_id")
    .notNull()
    .references(() => users.id),
  contentType: text("content_type", {
    enum: [
      "how-to",
      "reference",
      "adr",
      "runbook",
      "faq",
      "policy",
      "onboarding",
    ],
  })
    .notNull()
    .default("reference"),
  difficulty: text("difficulty", {
    enum: ["beginner", "intermediate", "advanced"],
  })
    .notNull()
    .default("beginner"),
  audience: text("audience"),
  readTimeMinutes: integer("read_time_minutes"),
  publishedAt: integer("published_at", { mode: "timestamp" }),
  lastReviewedAt: integer("last_reviewed_at", { mode: "timestamp" }),
  reviewIntervalDays: integer("review_interval_days").notNull().default(90),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
  updatedAt: integer("updated_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
  indexedAt: integer("indexed_at", { mode: "timestamp" }),
});

export const articleVersions = sqliteTable("article_versions", {
  id: text("id").primaryKey(),
  articleId: text("article_id")
    .notNull()
    .references(() => articles.id, { onDelete: "cascade" }),
  title: text("title").notNull(),
  content: text("content", { mode: "json" }).$type<Record<string, unknown>>(),
  changedBy: text("changed_by")
    .notNull()
    .references(() => users.id),
  changeSummary: text("change_summary"),
  version: integer("version").notNull(),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

export const articleTags = sqliteTable("article_tags", {
  articleId: text("article_id")
    .notNull()
    .references(() => articles.id, { onDelete: "cascade" }),
  tagId: text("tag_id")
    .notNull()
    .references(() => tags.id, { onDelete: "cascade" }),
});

// ─── Feedback ───────────────────────────────────────────────────────────────

export const articleFeedback = sqliteTable("article_feedback", {
  id: text("id").primaryKey(),
  articleId: text("article_id")
    .notNull()
    .references(() => articles.id, { onDelete: "cascade" }),
  userId: text("user_id").references(() => users.id),
  helpful: integer("helpful", { mode: "boolean" }).notNull(),
  comment: text("comment"),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

// ─── Analytics ──────────────────────────────────────────────────────────────

export const articleViews = sqliteTable("article_views", {
  id: text("id").primaryKey(),
  articleId: text("article_id")
    .notNull()
    .references(() => articles.id, { onDelete: "cascade" }),
  userId: text("user_id").references(() => users.id),
  sessionId: text("session_id"),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

export const searchQueries = sqliteTable("search_queries", {
  id: text("id").primaryKey(),
  query: text("query").notNull(),
  userId: text("user_id").references(() => users.id),
  resultsCount: integer("results_count").notNull().default(0),
  clickedArticleId: text("clicked_article_id").references(() => articles.id),
  searchType: text("search_type", {
    enum: ["fulltext", "semantic", "hybrid", "rag"],
  })
    .notNull()
    .default("fulltext"),
  responseTimeMs: integer("response_time_ms"),
  createdAt: integer("created_at", { mode: "timestamp" })
    .notNull()
    .$defaultFn(() => new Date()),
});

// ─── Relations ──────────────────────────────────────────────────────────────

export const usersRelations = relations(users, ({ many }) => ({
  articles: many(articles),
}));

export const articlesRelations = relations(articles, ({ one, many }) => ({
  owner: one(users, {
    fields: [articles.ownerId],
    references: [users.id],
  }),
  versions: many(articleVersions),
  tags: many(articleTags),
  feedback: many(articleFeedback),
  views: many(articleViews),
}));

export const articleVersionsRelations = relations(
  articleVersions,
  ({ one }) => ({
    article: one(articles, {
      fields: [articleVersions.articleId],
      references: [articles.id],
    }),
    changedByUser: one(users, {
      fields: [articleVersions.changedBy],
      references: [users.id],
    }),
  })
);

export const articleTagsRelations = relations(articleTags, ({ one }) => ({
  article: one(articles, {
    fields: [articleTags.articleId],
    references: [articles.id],
  }),
  tag: one(tags, {
    fields: [articleTags.tagId],
    references: [tags.id],
  }),
}));

export const tagsRelations = relations(tags, ({ many }) => ({
  articles: many(articleTags),
}));
