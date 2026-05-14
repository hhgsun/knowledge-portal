import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, articleVersions, articleViews } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { deleteArticleChunks } from "@/lib/search/qdrant";
import { eq, desc, count } from "drizzle-orm";
import { nanoid } from "nanoid";
import { z } from "zod";

const updateArticleSchema = z.object({
  title: z.string().min(1).max(300).optional(),
  content: z.record(z.unknown()).optional(),
  excerpt: z.string().max(500).optional(),
  status: z.enum(["draft", "in_review", "published", "archived"]).optional(),
  contentType: z
    .enum(["how-to", "reference", "adr", "runbook", "faq", "policy", "onboarding"])
    .optional(),
  difficulty: z.enum(["beginner", "intermediate", "advanced"]).optional(),
  audience: z.string().max(200).nullable().optional(),
  changeSummary: z.string().max(500).optional(),
});

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  const article = await db
    .select()
    .from(articles)
    .where(eq(articles.id, id))
    .get();

  if (!article) {
    // Try by slug
    const bySlug = await db
      .select()
      .from(articles)
      .where(eq(articles.slug, id))
      .get();
    if (!bySlug) {
      return NextResponse.json({ error: "Not found" }, { status: 404 });
    }
    // Record view
    await db.insert(articleViews).values({
      id: nanoid(),
      articleId: bySlug.id,
      userId: reqAuth.userId,
    });
    return NextResponse.json(bySlug);
  }

  // Record view
  await db.insert(articleViews).values({
    id: nanoid(),
    articleId: article.id,
    userId: reqAuth.userId,
  });

  return NextResponse.json(article);
}

export async function PUT(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  const article = await db
    .select()
    .from(articles)
    .where(eq(articles.id, id))
    .get();

  if (!article) {
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  const role = (session.user as { role: Role }).role;
  const isOwner = article.ownerId === session.user.id;

  if (!isOwner && !hasPermission(role, "articles:edit_any")) {
    if (!hasPermission(role, "articles:edit_own")) {
      return NextResponse.json({ error: "Forbidden" }, { status: 403 });
    }
  }

  try {
    const body = await request.json();
    const parsed = updateArticleSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    const data = parsed.data;
    const now = new Date();

    const updates: Record<string, unknown> = { updatedAt: now };
    if (data.title !== undefined) updates.title = data.title;
    if (data.content !== undefined) updates.content = data.content;
    if (data.excerpt !== undefined) updates.excerpt = data.excerpt;
    if (data.contentType !== undefined) updates.contentType = data.contentType;
    if (data.difficulty !== undefined) updates.difficulty = data.difficulty;
    if (data.audience !== undefined) updates.audience = data.audience;

    if (data.status !== undefined) {
      updates.status = data.status;
      if (data.status === "published" && article.status !== "published") {
        updates.publishedAt = now;
        updates.lastReviewedAt = now;
      }
    }

    await db.update(articles).set(updates).where(eq(articles.id, id));

    // Create version on content change
    if (data.content !== undefined || data.title !== undefined) {
      const versionCount = await db
        .select({ count: count() })
        .from(articleVersions)
        .where(eq(articleVersions.articleId, id))
        .get();

      await db.insert(articleVersions).values({
        id: nanoid(),
        articleId: id,
        title: data.title || article.title,
        content: data.content || article.content,
        changedBy: session.user.id,
        changeSummary: data.changeSummary || "Content updated",
        version: (versionCount?.count || 0) + 1,
      });
    }

    const updated = await db
      .select()
      .from(articles)
      .where(eq(articles.id, id))
      .get();

    return NextResponse.json(updated);
  } catch {
    return NextResponse.json(
      { error: "Internal server error" },
      { status: 500 }
    );
  }
}

export async function DELETE(
  _request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;
  const article = await db
    .select()
    .from(articles)
    .where(eq(articles.id, id))
    .get();

  if (!article) {
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  const role = (session.user as { role: Role }).role;
  const isOwner = article.ownerId === session.user.id;

  if (!isOwner && !hasPermission(role, "articles:delete_any")) {
    if (!hasPermission(role, "articles:delete_own")) {
      return NextResponse.json({ error: "Forbidden" }, { status: 403 });
    }
  }

  // Clean up Qdrant vectors and FTS5 index before deleting
  try {
    await deleteArticleChunks(id);
  } catch {
    // Qdrant may not be available — proceed with DB deletion
  }

  try {
    const client = (db as unknown as { $client: { execute: (sql: string, args?: unknown[]) => Promise<unknown> } }).$client;
    const result = await client.execute(
      `SELECT rowid FROM articles WHERE id = ?`,
      [id]
    ) as { rows: unknown[][] };
    if (result.rows.length > 0) {
      const rowid = result.rows[0][0] as number;
      await client.execute(
        `INSERT INTO articles_fts(articles_fts, rowid, title, excerpt, plain_text) VALUES ('delete', ?, ?, ?, ?)`,
        [rowid, article.title, article.excerpt || "", ""]
      );
    }
  } catch {
    // FTS5 table may not exist yet — proceed with DB deletion
  }

  await db.delete(articles).where(eq(articles.id, id));

  return NextResponse.json({ success: true });
}
