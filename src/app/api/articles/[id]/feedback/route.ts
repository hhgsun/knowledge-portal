import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articleFeedback } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { eq, and, count } from "drizzle-orm";
import { nanoid } from "nanoid";
import { z } from "zod";

const feedbackSchema = z.object({
  helpful: z.boolean(),
  comment: z.string().max(1000).optional(),
});

export async function POST(
  request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;

  try {
    const body = await request.json();
    const parsed = feedbackSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json(
        { error: "Invalid input", details: parsed.error.flatten() },
        { status: 400 }
      );
    }

    await db.insert(articleFeedback).values({
      id: nanoid(),
      articleId: id,
      userId: session.user.id,
      helpful: parsed.data.helpful,
      comment: parsed.data.comment || null,
    });

    return NextResponse.json({ success: true });
  } catch {
    return NextResponse.json(
      { error: "Internal server error" },
      { status: 500 }
    );
  }
}

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ id: string }> }
) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const { id } = await params;

  const [helpful, notHelpful] = await Promise.all([
    db
      .select({ count: count() })
      .from(articleFeedback)
      .where(
        and(
          eq(articleFeedback.articleId, id),
          eq(articleFeedback.helpful, true)
        )
      )
      .get(),
    db
      .select({ count: count() })
      .from(articleFeedback)
      .where(
        and(
          eq(articleFeedback.articleId, id),
          eq(articleFeedback.helpful, false)
        )
      )
      .get(),
  ]);

  return NextResponse.json({
    helpful: helpful?.count || 0,
    notHelpful: notHelpful?.count || 0,
  });
}
