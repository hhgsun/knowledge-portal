import { db } from "@/lib/db";
import { apiKeys, users } from "@/lib/db/schema";
import { eq } from "drizzle-orm";
import { compareSync } from "bcryptjs";

interface ApiKeyAuth {
  userId: string;
  role: string;
  permissions: string[];
}

/**
 * Validate an API key from the Authorization header.
 * Returns user info if valid, null otherwise.
 */
export async function validateApiKey(request: Request): Promise<ApiKeyAuth | null> {
  const authHeader = request.headers.get("authorization");
  if (!authHeader?.startsWith("Bearer kp_")) {
    return null;
  }

  const rawKey = authHeader.slice(7); // Remove "Bearer "

  // Fetch all non-expired keys (in practice, limit scope with indexed lookup)
  const allKeys = await db
    .select()
    .from(apiKeys)
    .all();

  for (const key of allKeys) {
    // Check expiration
    if (key.expiresAt && new Date(key.expiresAt) < new Date()) {
      continue;
    }

    // Compare hash
    if (compareSync(rawKey, key.keyHash)) {
      // Update last used timestamp
      await db
        .update(apiKeys)
        .set({ lastUsedAt: new Date() })
        .where(eq(apiKeys.id, key.id));

      // Get user info
      const [user] = await db
        .select({ id: users.id, role: users.role })
        .from(users)
        .where(eq(users.id, key.userId))
        .limit(1);

      if (!user) return null;

      return {
        userId: user.id,
        role: user.role,
        permissions: (key.permissions as string[]) || [],
      };
    }
  }

  return null;
}
