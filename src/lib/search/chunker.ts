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

/**
 * Chunks a TipTap JSON document by heading sections.
 * Each chunk contains the text under a heading (or the intro before the first heading).
 */
export function chunkDocument(
  doc: Record<string, unknown>,
  title: string
): Chunk[] {
  const content = (doc.content || []) as ContentNode[];
  const chunks: Chunk[] = [];
  let currentHeading = title;
  let currentText = "";
  let chunkIndex = 0;

  for (const node of content) {
    if (node.type === "heading") {
      // Save previous chunk
      if (currentText.trim()) {
        chunks.push({
          text: `${currentHeading}\n\n${currentText.trim()}`,
          heading: currentHeading,
          index: chunkIndex++,
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
    chunks.push({
      text: `${currentHeading}\n\n${currentText.trim()}`,
      heading: currentHeading,
      index: chunkIndex,
    });
  }

  // If no chunks were created (empty doc), create one from title
  if (chunks.length === 0 && title) {
    chunks.push({ text: title, heading: title, index: 0 });
  }

  return chunks;
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
