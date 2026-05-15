import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, articleTags, articleVersions } from "@/lib/db/schema";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { eq, desc, and, like, count } from "drizzle-orm";
import { nanoid } from "nanoid";
import slugify from "slugify";
import { z } from "zod";

const createArticleSchema = z.object({
  title: z.string().min(1).max(300),
  content: z.record(z.unknown()).optional(),
  excerpt: z.string().max(500).optional(),
  status: z.enum(["draft", "in_review", "published", "archived"]).default("draft"),
  contentType: z
    .enum(["how-to", "reference", "adr", "runbook", "faq", "policy", "onboarding"])
    .default("reference"),
  difficulty: z.enum(["beginner", "intermediate", "advanced"]).default("beginner"),
  audience: z.string().max(200).optional(),
  tags: z.array(z.string()).optional(),
});

export async function GET(request: Request) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { searchParams } = new URL(request.url);
  const page = parseInt(searchParams.get("page") || "1");
  const limit = Math.min(parseInt(searchParams.get("limit") || "20"), 100);
  const status = searchParams.get("status");
  const search = searchParams.get("q");
  const offset = (page - 1) * limit;

  const conditions = [];
  if (status) conditions.push(eq(articles.status, status as "draft" | "in_review" | "published" | "archived"));
  if (search) conditions.push(like(articles.title, `%${search}%`));

  // Viewers can only see published articles
  const role = reqAuth.role;
  if (role === "viewer") {
    conditions.push(eq(articles.status, "published"));
  }

  const results = await db
    .select()
    .from(articles)
    .where(conditions.length > 0 ? and(...conditions) : undefined)
    .orderBy(desc(articles.updatedAt))
    .limit(limit)
    .offset(offset)
    .all();

  const total = await db
    .select({ count: count() })
    .from(articles)
    .where(conditions.length > 0 ? and(...conditions) : undefined)
    .get();

  return NextResponse.json({
    articles: results,
    pagination: {
      page,
      limit,
      total: total?.count || 0,
      pages: Math.ceil((total?.count || 0) / limit),
    },
  });
}

export async function POST(request: Request) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = reqAuth.role;
  if (!hasPermission(role, "articles:create")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  try {
    const body = await request.json();
    const parsed = createArticleSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    const data = parsed.data;
    const id = nanoid();
    const slug = slugify(data.title, { lower: true, strict: true }) + "-" + id.slice(0, 6);

    const now = new Date();
    const article = {
      id,
      title: data.title,
      slug,
      content: data.content || null,
      excerpt: data.excerpt || null,
      status: data.status,
      ownerId: reqAuth.userId,
      contentType: data.contentType,
      difficulty: data.difficulty,
      audience: data.audience || null,
      publishedAt: data.status === "published" ? now : null,
      lastReviewedAt: data.status === "published" ? now : null,
      createdAt: now,
      updatedAt: now,
    };

    await db.insert(articles).values(article);

    // Create initial version
    await db.insert(articleVersions).values({
      id: nanoid(),
      articleId: id,
      title: data.title,
      content: data.content || null,
      changedBy: reqAuth.userId,
      changeSummary: "Initial creation",
      version: 1,
    });

    // Add tags if provided
    if (data.tags && data.tags.length > 0) {
      await db.insert(articleTags).values(
        data.tags.map((tagId) => ({ articleId: id, tagId }))
      );
    }

    return NextResponse.json(article, { status: 201 });
  } catch {
    return NextResponse.json(
      { error: "Internal server error" },
      { status: 500 }
    );
  }
}
