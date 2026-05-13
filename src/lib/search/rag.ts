import { generateEmbedding } from "./embeddings";
import { searchSimilar } from "./qdrant";

const OLLAMA_URL = process.env.OLLAMA_URL || "http://localhost:11434";
const LLM_MODEL = "llama3";

interface RAGResult {
  answer: string;
  sources: { articleId: string; text: string; score: number }[];
}

/**
 * RAG pipeline: Retrieve relevant chunks, then generate an answer using the LLM.
 */
export async function ragQuery(query: string): Promise<RAGResult> {
  // 1. Generate query embedding
  const queryVector = await generateEmbedding(query);

  // 2. Retrieve top-K relevant chunks from Qdrant
  const results = await searchSimilar(queryVector, 5);

  const sources = results.map((r) => ({
    articleId: (r.payload?.articleId as string) || "",
    text: (r.payload?.text as string) || "",
    score: r.score,
  }));

  if (sources.length === 0) {
    return {
      answer: "I couldn't find relevant information in the knowledge base for your query.",
      sources: [],
    };
  }

  // 3. Build context from retrieved chunks
  const context = sources
    .map((s, i) => `[Source ${i + 1}]: ${s.text}`)
    .join("\n\n");

  // 4. Generate answer using LLM
  const prompt = `You are a helpful knowledge base assistant. Answer the user's question based ONLY on the provided context. If the context doesn't contain enough information, say so. Cite sources using [Source N] notation.

Context:
${context}

Question: ${query}

Answer:`;

  const response = await fetch(`${OLLAMA_URL}/api/generate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      model: LLM_MODEL,
      prompt,
      stream: false,
    }),
  });

  if (!response.ok) {
    throw new Error(`LLM generation failed: ${response.status}`);
  }

  const data = await response.json();

  return {
    answer: data.response || "Unable to generate answer.",
    sources,
  };
}
