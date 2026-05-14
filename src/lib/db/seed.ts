import { db } from "./index";
import { users, tags } from "./schema";
import { hashSync } from "bcryptjs";
import { nanoid } from "nanoid";

async function seed() {
  console.log("🌱 Seeding database...");

  // Create admin user
  const adminId = nanoid();
  await db
    .insert(users)
    .values({
      id: adminId,
      name: "Admin",
      email: "admin@knowledge.local",
      passwordHash: hashSync("admin123", 12),
      role: "admin",
    })
    .onConflictDoNothing();
  console.log("  ✓ Admin user created (admin@knowledge.local / admin123)");

  // Create default tags
  const defaultTags = [
    "getting-started",
    "tutorial",
    "troubleshooting",
    "best-practices",
    "api",
    "deployment",
    "security",
    "performance",
    "testing",
    "monitoring",
  ];

  for (const tag of defaultTags) {
    await db
      .insert(tags)
      .values({ id: nanoid(), name: tag.replace(/-/g, " "), slug: tag })
      .onConflictDoNothing();
  }
  console.log("  ✓ Tags created");

  console.log("✅ Seed complete!");
}

seed().catch(console.error);
