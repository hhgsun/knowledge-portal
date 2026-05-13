import { createClient } from "@libsql/client";
import { drizzle } from "drizzle-orm/libsql";
import * as schema from "./schema";

const dbUrl = process.env.DATABASE_URL || "./data/knowledge.db";

const client = createClient({
  url: `file:${dbUrl}`,
});

export const db = drizzle(client, { schema });
export type DB = typeof db;
