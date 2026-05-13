import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { apiKeys } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { eq, desc } from "drizzle-orm";
import { nanoid } from "nanoid";
import { hashSync } from "bcryptjs";
import { z } from "zod";

const createKeySchema = z.object({
  name: z.string().min(1).max(100),
  permissions: z.array(z.string()).optional(),
  expiresInDays: z.number().int().min(1).max(365).optional(),
});

export async function GET() {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "api_keys:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const keys = await db
    .select({
      id: apiKeys.id,
      name: apiKeys.name,
      permissions: apiKeys.permissions,
      lastUsedAt: apiKeys.lastUsedAt,
      expiresAt: apiKeys.expiresAt,
      createdAt: apiKeys.createdAt,
    })
    .from(apiKeys)
    .where(eq(apiKeys.userId, session.user.id))
    .orderBy(desc(apiKeys.createdAt))
    .all();

  return NextResponse.json(keys);
}

export async function POST(request: Request) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "api_keys:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  try {
    const body = await request.json();
    const parsed = createKeySchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    // Generate API key: kp_ prefix + random string
    const rawKey = `kp_${nanoid(32)}`;
    const keyHash = hashSync(rawKey, 10);

    const id = nanoid();
    const expiresAt = parsed.data.expiresInDays
      ? new Date(Date.now() + parsed.data.expiresInDays * 24 * 60 * 60 * 1000)
      : null;

    await db.insert(apiKeys).values({
      id,
      userId: session.user.id,
      keyHash,
      name: parsed.data.name,
      permissions: parsed.data.permissions || ["articles:read", "search"],
      expiresAt,
    });

    // Return the raw key only once — it cannot be retrieved later
    return NextResponse.json(
      {
        id,
        key: rawKey,
        name: parsed.data.name,
        permissions: parsed.data.permissions || ["articles:read", "search"],
        expiresAt,
      },
      { status: 201 }
    );
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
  if (!hasPermission(role, "api_keys:manage")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const { searchParams } = new URL(request.url);
  const id = searchParams.get("id");
  if (!id) {
    return NextResponse.json({ error: "Key ID required" }, { status: 400 });
  }

  // Ensure the key belongs to the user
  const key = await db
    .select()
    .from(apiKeys)
    .where(eq(apiKeys.id, id))
    .get();

  if (!key || key.userId !== session.user.id) {
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  await db.delete(apiKeys).where(eq(apiKeys.id, id));

  return NextResponse.json({ success: true });
}
