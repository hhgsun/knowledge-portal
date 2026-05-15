import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articleVersions } from "@/lib/db/schema";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { eq, and } from "drizzle-orm";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string; versionId: string }> }
) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id, versionId } = await params;

  const version = await db
    .select()
    .from(articleVersions)
    .where(
      and(
        eq(articleVersions.articleId, id),
        eq(articleVersions.id, versionId)
      )
    )
    .get();

  if (!version) {
    return NextResponse.json({ error: "Not found" }, { status: 404 });
  }

  return NextResponse.json(version);
}
