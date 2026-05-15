DROP TABLE `article_categories`;--> statement-breakpoint
DROP TABLE `categories`;--> statement-breakpoint
DROP TABLE `sessions`;--> statement-breakpoint
PRAGMA foreign_keys=OFF;--> statement-breakpoint
CREATE TABLE `__new_articles` (
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
	`created_via_api_key_id` text,
	`read_time_minutes` integer,
	`published_at` integer,
	`last_reviewed_at` integer,
	`review_interval_days` integer DEFAULT 90 NOT NULL,
	`created_at` integer NOT NULL,
	`updated_at` integer NOT NULL,
	`indexed_at` integer,
	FOREIGN KEY (`owner_id`) REFERENCES `users`(`id`) ON UPDATE no action ON DELETE no action,
	FOREIGN KEY (`created_via_api_key_id`) REFERENCES `api_keys`(`id`) ON UPDATE no action ON DELETE set null
);
--> statement-breakpoint
INSERT INTO `__new_articles`("id", "title", "slug", "content", "excerpt", "status", "owner_id", "content_type", "difficulty", "audience", "created_via_api_key_id", "read_time_minutes", "published_at", "last_reviewed_at", "review_interval_days", "created_at", "updated_at", "indexed_at") SELECT "id", "title", "slug", "content", "excerpt", "status", "owner_id", "content_type", "difficulty", "audience", "created_via_api_key_id", "read_time_minutes", "published_at", "last_reviewed_at", "review_interval_days", "created_at", "updated_at", "indexed_at" FROM `articles`;--> statement-breakpoint
DROP TABLE `articles`;--> statement-breakpoint
ALTER TABLE `__new_articles` RENAME TO `articles`;--> statement-breakpoint
PRAGMA foreign_keys=ON;--> statement-breakpoint
CREATE UNIQUE INDEX `articles_slug_unique` ON `articles` (`slug`);