import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { tags, articleTags } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { eq, asc, count } from "drizzle-orm";
import { nanoid } from "nanoid";
import slugify from "slugify";
import { z } from "zod";

export async function GET() {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const allTags = await db
    .select({
      id: tags.id,
      name: tags.name,
      slug: tags.slug,
      articleCount: count(articleTags.articleId),
    })
    .from(tags)
    .leftJoin(articleTags, eq(tags.id, articleTags.tagId))
    .groupBy(tags.id)
    .orderBy(asc(tags.name))
    .all();

  return NextResponse.json(allTags);
}

const createTagSchema = z.object({
  name: z.string().min(1).max(50),
});

export async function POST(request: Request) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "tags:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  try {
    const body = await request.json();
    const parsed = createTagSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    const slug = slugify(parsed.data.name, { lower: true, strict: true });

    // Check for duplicate
    const existing = await db
      .select()
      .from(tags)
      .where(eq(tags.slug, slug))
      .get();

    if (existing) {
      return NextResponse.json(existing);
    }

    const id = nanoid();
    await db.insert(tags).values({
      id,
      name: parsed.data.name,
      slug,
    });

    const [created] = await db.select().from(tags).where(eq(tags.id, id)).limit(1);
    return NextResponse.json(created, { status: 201 });
  } catch {
    return NextResponse.json(
      { error: "Internal server error" },
      { status: 500 }
    );
  }
}

export async function DELETE(request: Request) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "tags:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const { searchParams } = new URL(request.url);
  const id = searchParams.get("id");
  if (!id) {
    return NextResponse.json({ error: "Tag ID required" }, { status: 400 });
  }

  await db.delete(articleTags).where(eq(articleTags.tagId, id));
  await db.delete(tags).where(eq(tags.id, id));

  return NextResponse.json({ success: true });
}
