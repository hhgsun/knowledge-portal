-- FTS5 virtual table for full-text search
CREATE VIRTUAL TABLE IF NOT EXISTS articles_fts USING fts5(
  title,
  excerpt,
  plain_text,
  content='articles',
  content_rowid='rowid',
  tokenize='porter unicode61'
);
--> statement-breakpoint
-- Triggers to keep FTS index in sync with articles table
CREATE TRIGGER IF NOT EXISTS articles_fts_insert AFTER INSERT ON articles BEGIN
  INSERT INTO articles_fts(rowid, title, excerpt, plain_text)
  VALUES (NEW.rowid, NEW.title, COALESCE(NEW.excerpt, ''), '');
END;
--> statement-breakpoint
CREATE TRIGGER IF NOT EXISTS articles_fts_update AFTER UPDATE ON articles BEGIN
  INSERT INTO articles_fts(articles_fts, rowid, title, excerpt, plain_text)
  VALUES ('delete', OLD.rowid, OLD.title, COALESCE(OLD.excerpt, ''), '');
  INSERT INTO articles_fts(rowid, title, excerpt, plain_text)
  VALUES (NEW.rowid, NEW.title, COALESCE(NEW.excerpt, ''), '');
END;
--> statement-breakpoint
CREATE TRIGGER IF NOT EXISTS articles_fts_delete AFTER DELETE ON articles BEGIN
  INSERT INTO articles_fts(articles_fts, rowid, title, excerpt, plain_text)
  VALUES ('delete', OLD.rowid, OLD.title, COALESCE(OLD.excerpt, ''), '');
END;
