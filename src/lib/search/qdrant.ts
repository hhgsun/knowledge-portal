import { QdrantClient } from "@qdrant/js-client-rest";

const qdrantUrl = process.env.QDRANT_URL || "http://localhost:6333";

export const qdrant = new QdrantClient({ url: qdrantUrl });

export const COLLECTION_NAME = "knowledge_articles";
export const VECTOR_SIZE = 768; // nomic-embed-text dimension

export async function ensureCollection() {
  const collections = await qdrant.getCollections();
  const exists = collections.collections.some(
    (c) => c.name === COLLECTION_NAME
  );

  if (!exists) {
    await qdrant.createCollection(COLLECTION_NAME, {
      vectors: {
        size: VECTOR_SIZE,
        distance: "Cosine",
      },
    });
    console.log(`Created Qdrant collection: ${COLLECTION_NAME}`);
  }
}

export async function upsertChunks(
  articleId: string,
  chunks: { id: string; text: string; vector: number[]; metadata: Record<string, unknown> }[]
) {
  if (chunks.length === 0) return;

  await qdrant.upsert(COLLECTION_NAME, {
    wait: true,
    points: chunks.map((chunk) => ({
      id: chunk.id,
      vector: chunk.vector,
      payload: {
        articleId,
        text: chunk.text,
        ...chunk.metadata,
      },
    })),
  });
}

export async function searchSimilar(
  queryVector: number[],
  limit: number = 10,
  filter?: Record<string, unknown>
) {
  const results = await qdrant.search(COLLECTION_NAME, {
    vector: queryVector,
    limit,
    with_payload: true,
    filter: filter || undefined,
  });

  return results;
}

export async function deleteArticleChunks(articleId: string) {
  await qdrant.delete(COLLECTION_NAME, {
    wait: true,
    filter: {
      must: [
        {
          key: "articleId",
          match: { value: articleId },
        },
      ],
    },
  });
}
