import { auth } from "./config";

export type Role = "admin" | "editor" | "viewer";

export type Permission =
  | "articles:create"
  | "articles:edit_own"
  | "articles:edit_any"
  | "articles:delete_own"
  | "articles:delete_any"
  | "articles:publish"
  | "articles:archive"
  | "categories:manage"
  | "tags:manage"
  | "users:manage"
  | "analytics:view"
  | "api_keys:manage";

const rolePermissions: Record<Role, Permission[]> = {
  admin: [
    "articles:create",
    "articles:edit_own",
    "articles:edit_any",
    "articles:delete_own",
    "articles:delete_any",
    "articles:publish",
    "articles:archive",
    "categories:manage",
    "tags:manage",
    "users:manage",
    "analytics:view",
    "api_keys:manage",
  ],
  editor: [
    "articles:create",
    "articles:edit_own",
    "articles:delete_own",
    "articles:publish",
    "articles:archive",
    "tags:manage",
    "analytics:view",
  ],
  viewer: [],
};

export function hasPermission(role: Role, permission: Permission): boolean {
  return rolePermissions[role]?.includes(permission) ?? false;
}

export async function requirePermission(permission: Permission) {
  const session = await auth();
  if (!session?.user) {
    throw new Error("Unauthorized");
  }
  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, permission)) {
    throw new Error("Forbidden");
  }
  return session;
}

export async function requireAuth() {
  const session = await auth();
  if (!session?.user) {
    throw new Error("Unauthorized");
  }
  return session;
}
