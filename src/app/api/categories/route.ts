import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { categories } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { eq, isNull, asc } from "drizzle-orm";
import { nanoid } from "nanoid";
import slugify from "slugify";
import { z } from "zod";

export async function GET() {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  // Fetch all categories and build tree
  const allCategories = await db
    .select()
    .from(categories)
    .orderBy(asc(categories.sortOrder), asc(categories.name))
    .all();

  // Build hierarchical tree
  const topLevel = allCategories.filter((c) => !c.parentId);
  const tree = topLevel.map((parent) => ({
    ...parent,
    children: allCategories.filter((c) => c.parentId === parent.id),
  }));

  return NextResponse.json(tree);
}

const createCategorySchema = z.object({
  name: z.string().min(1).max(100),
  parentId: z.string().nullable().optional(),
  description: z.string().max(500).optional(),
  icon: z.string().max(50).optional(),
  sortOrder: z.number().int().optional(),
});

export async function POST(request: Request) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "categories:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  try {
    const body = await request.json();
    const parsed = createCategorySchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    const data = parsed.data;
    const id = nanoid();
    const slug = slugify(data.name, { lower: true, strict: true });

    const category = {
      id,
      name: data.name,
      slug,
      parentId: data.parentId || null,
      description: data.description || null,
      icon: data.icon || null,
      sortOrder: data.sortOrder || 0,
    };

    await db.insert(categories).values(category);

    return NextResponse.json(category, { status: 201 });
  } catch {
    return NextResponse.json(
      { error: "Internal server error" },
      { status: 500 }
    );
  }
}
