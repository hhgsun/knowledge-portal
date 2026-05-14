interface ContentNode {
  type?: string;
  text?: string;
  content?: ContentNode[];
  attrs?: Record<string, unknown>;
}

export interface Chunk {
  text: string;
  heading: string;
  index: number;
}

// Max characters per chunk (~500 tokens). Chunks exceeding this will be split.
const MAX_CHUNK_CHARS = 1500;
// Overlap characters between split sub-chunks to preserve context continuity.
const OVERLAP_CHARS = 200;

/**
 * Chunks a TipTap JSON document by heading sections.
 * Each chunk contains the text under a heading (or the intro before the first heading).
 * Chunks exceeding MAX_CHUNK_CHARS are split with overlap.
 */
export function chunkDocument(
  doc: Record<string, unknown>,
  title: string
): Chunk[] {
  const content = (doc.content || []) as ContentNode[];
  const rawChunks: { text: string; heading: string }[] = [];
  let currentHeading = title;
  let currentText = "";

  for (const node of content) {
    if (node.type === "heading") {
      // Save previous chunk
      if (currentText.trim()) {
        rawChunks.push({
          text: `${currentHeading}\n\n${currentText.trim()}`,
          heading: currentHeading,
        });
      }
      currentHeading = extractText(node);
      currentText = "";
    } else {
      currentText += extractText(node) + "\n";
    }
  }

  // Save last chunk
  if (currentText.trim()) {
    rawChunks.push({
      text: `${currentHeading}\n\n${currentText.trim()}`,
      heading: currentHeading,
    });
  }

  // If no chunks were created (empty doc), create one from title
  if (rawChunks.length === 0 && title) {
    rawChunks.push({ text: title, heading: title });
  }

  // Split oversized chunks
  const chunks: Chunk[] = [];
  let chunkIndex = 0;

  for (const raw of rawChunks) {
    if (raw.text.length <= MAX_CHUNK_CHARS) {
      chunks.push({ text: raw.text, heading: raw.heading, index: chunkIndex++ });
    } else {
      const subChunks = splitWithOverlap(raw.text, MAX_CHUNK_CHARS, OVERLAP_CHARS);
      for (const sub of subChunks) {
        chunks.push({ text: sub, heading: raw.heading, index: chunkIndex++ });
      }
    }
  }

  return chunks;
}

/**
 * Split text into segments of maxLen with overlap, breaking at sentence boundaries when possible.
 */
function splitWithOverlap(text: string, maxLen: number, overlap: number): string[] {
  const segments: string[] = [];
  let start = 0;

  while (start < text.length) {
    let end = Math.min(start + maxLen, text.length);

    // Try to break at sentence boundary (. ! ? followed by space/newline)
    if (end < text.length) {
      const slice = text.slice(start, end);
      const lastSentenceEnd = Math.max(
        slice.lastIndexOf(". "),
        slice.lastIndexOf(".\n"),
        slice.lastIndexOf("! "),
        slice.lastIndexOf("? "),
      );
      if (lastSentenceEnd > maxLen * 0.5) {
        end = start + lastSentenceEnd + 1;
      }
    }

    segments.push(text.slice(start, end).trim());

    // Move start forward, subtracting overlap
    start = end - overlap;
    if (start >= text.length) break;
    // Prevent infinite loop
    if (end === text.length) break;
  }

  return segments;
}

/**
 * Recursively extract plain text from a TipTap node.
 */
function extractText(node: ContentNode): string {
  if (node.text) return node.text;
  if (!node.content) return "";
  return node.content.map(extractText).join("");
}

/**
 * Extract all plain text from a TipTap JSON document (for FTS indexing).
 */
export function extractPlainText(doc: Record<string, unknown>): string {
  const content = (doc.content || []) as ContentNode[];
  return content.map(extractText).join("\n").trim();
}
