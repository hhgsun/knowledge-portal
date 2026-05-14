-- Recreate articles table without category_id (SQLite can't DROP COLUMN with FK)
CREATE TABLE `articles_new` (
    `id` text PRIMARY KEY NOT NULL,
    `title` text NOT NULL,
    `slug` text NOT NULL,
    `content` text,
    `excerpt` text,
    `status` text DEFAULT 'draft' NOT NULL,
    `owner_id` text NOT NULL,
    `content_type` text DEFAULT 'reference' NOT NULL,
    `difficulty` text DEFAULT 'beginner' NOT NULL,
    `audience` text,
    `read_time_minutes` integer,
    `published_at` integer,
    `last_reviewed_at` integer,
    `review_interval_days` integer DEFAULT 90 NOT NULL,
    `created_at` integer NOT NULL,
    `updated_at` integer NOT NULL,
    `indexed_at` integer,
    FOREIGN KEY (`owner_id`) REFERENCES `users`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
INSERT INTO `articles_new` (`id`, `title`, `slug`, `content`, `excerpt`, `status`, `owner_id`, `content_type`, `difficulty`, `audience`, `read_time_minutes`, `published_at`, `last_reviewed_at`, `review_interval_days`, `created_at`, `updated_at`, `indexed_at`)
SELECT `id`, `title`, `slug`, `content`, `excerpt`, `status`, `owner_id`, `content_type`, `difficulty`, `audience`, `read_time_minutes`, `published_at`, `last_reviewed_at`, `review_interval_days`, `created_at`, `updated_at`, `indexed_at`
FROM `articles`;
--> statement-breakpoint
DROP TABLE `articles`;
--> statement-breakpoint
ALTER TABLE `articles_new` RENAME TO `articles`;
--> statement-breakpoint
CREATE UNIQUE INDEX `articles_slug_unique` ON `articles` (`slug`);
--> statement-breakpoint
DROP TABLE IF EXISTS `article_categories`;
--> statement-breakpoint
DROP TABLE IF EXISTS `categories`;