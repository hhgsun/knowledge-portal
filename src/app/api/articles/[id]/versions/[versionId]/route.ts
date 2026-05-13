import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articleVersions } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { eq, and } from "drizzle-orm";

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string; versionId: string }> }
) {
  const session = await auth();
  if (!session?.user) {
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
