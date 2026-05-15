import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articleVersions, users } from "@/lib/db/schema";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { eq, desc } from "drizzle-orm";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;

  const versions = await db
    .select({
      id: articleVersions.id,
      version: articleVersions.version,
      title: articleVersions.title,
      changeSummary: articleVersions.changeSummary,
      changedBy: articleVersions.changedBy,
      changedByName: users.name,
      createdAt: articleVersions.createdAt,
    })
    .from(articleVersions)
    .leftJoin(users, eq(articleVersions.changedBy, users.id))
    .where(eq(articleVersions.articleId, id))
    .orderBy(desc(articleVersions.version))
    .all();

  return NextResponse.json(versions);
}
