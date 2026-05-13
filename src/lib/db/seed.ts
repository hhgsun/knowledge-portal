import { db } from "./index";
import { users, categories, tags } from "./schema";
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

  // Create default categories
  const defaultCategories = [
    {
      id: nanoid(),
      name: "Engineering",
      slug: "engineering",
      icon: "code",
      sortOrder: 1,
    },
    {
      id: nanoid(),
      name: "Product",
      slug: "product",
      icon: "package",
      sortOrder: 2,
    },
    {
      id: nanoid(),
      name: "Operations",
      slug: "operations",
      icon: "settings",
      sortOrder: 3,
    },
    {
      id: nanoid(),
      name: "People & Culture",
      slug: "people-culture",
      icon: "users",
      sortOrder: 4,
    },
    {
      id: nanoid(),
      name: "Security & Compliance",
      slug: "security-compliance",
      icon: "shield",
      sortOrder: 5,
    },
    {
      id: nanoid(),
      name: "Customer Success",
      slug: "customer-success",
      icon: "heart",
      sortOrder: 6,
    },
  ];

  // Subcategories
  const subcategories = [
    { parent: "engineering", name: "Backend", slug: "backend" },
    { parent: "engineering", name: "Frontend", slug: "frontend" },
    { parent: "engineering", name: "Infrastructure", slug: "infrastructure" },
    { parent: "engineering", name: "DevOps", slug: "devops" },
    { parent: "engineering", name: "Architecture", slug: "architecture" },
    { parent: "product", name: "Features", slug: "features" },
    { parent: "product", name: "Roadmap", slug: "roadmap" },
    { parent: "product", name: "User Research", slug: "user-research" },
    { parent: "operations", name: "Processes", slug: "processes" },
    {
      parent: "operations",
      name: "Vendor Management",
      slug: "vendor-management",
    },
    {
      parent: "people-culture",
      name: "Onboarding",
      slug: "onboarding",
    },
    { parent: "people-culture", name: "Policies", slug: "policies" },
    { parent: "people-culture", name: "Benefits", slug: "benefits" },
    {
      parent: "security-compliance",
      name: "Security Policies",
      slug: "security-policies",
    },
    {
      parent: "security-compliance",
      name: "Incident Response",
      slug: "incident-response",
    },
    {
      parent: "security-compliance",
      name: "Data Privacy",
      slug: "data-privacy",
    },
    {
      parent: "customer-success",
      name: "Support Playbooks",
      slug: "support-playbooks",
    },
    {
      parent: "customer-success",
      name: "Customer FAQ",
      slug: "customer-faq",
    },
  ];

  for (const cat of defaultCategories) {
    await db.insert(categories).values(cat).onConflictDoNothing();
  }

  for (const sub of subcategories) {
    const parent = defaultCategories.find((c) => c.slug === sub.parent);
    if (parent) {
      await db
        .insert(categories)
        .values({
          id: nanoid(),
          name: sub.name,
          slug: sub.slug,
          parentId: parent.id,
          sortOrder: 0,
        })
        .onConflictDoNothing();
    }
  }
  console.log("  ✓ Categories created");

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
