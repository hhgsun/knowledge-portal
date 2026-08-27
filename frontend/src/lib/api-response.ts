const DEFAULT_API_ERROR = "The request could not be completed. Please try again.";
const API_UNAVAILABLE_ERROR = "The API is unavailable. Please try again shortly.";

type ApiErrorPayload = Record<string, unknown> & { error: string };

function statusFallback(status: number): string {
  if (status === 401) return "Your session has expired. Please sign in again.";
  if (status === 403) return "You do not have permission to perform this action.";
  if (status === 404) return "The requested resource could not be found.";
  if (status === 429) return "Too many requests. Please wait and try again.";
  if (status >= 500) return API_UNAVAILABLE_ERROR;
  return DEFAULT_API_ERROR;
}

function parseObject(text: string): Record<string, unknown> | null {
  if (!text.trim()) return null;

  try {
    const value: unknown = JSON.parse(text);
    return value !== null && typeof value === "object" && !Array.isArray(value)
      ? value as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

export function networkErrorMessage(): string {
  return typeof navigator !== "undefined" && !navigator.onLine
    ? "You are offline. Please check your connection."
    : API_UNAVAILABLE_ERROR;
}

export function apiErrorMessage(error: unknown, fallback = networkErrorMessage()): string {
  if (error instanceof TypeError) return networkErrorMessage();
  return error instanceof Error && error.message.trim() ? error.message : fallback;
}

export async function readApiJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  if (!text.trim()) {
    throw new Error(response.ok ? "The API returned an empty response." : statusFallback(response.status));
  }

  try {
    return JSON.parse(text) as T;
  } catch {
    throw new Error(response.ok ? "The API returned an invalid response." : statusFallback(response.status));
  }
}

export async function readApiError(response: Response, fallback = DEFAULT_API_ERROR): Promise<string> {
  try {
    const payload = await readApiJson<Record<string, unknown>>(response);
    return typeof payload.error === "string" && payload.error.trim()
      ? payload.error
      : fallback;
  } catch (error) {
    return error instanceof Error ? error.message : fallback;
  }
}

/**
 * Keep the existing Response-based API contract while guaranteeing that every
 * failed API response has a readable JSON `{ error }` body. Reverse proxies
 * commonly return an empty body or HTML while the backend is unavailable.
 */
export async function normalizeApiErrorResponse(
  response: Response,
): Promise<{ response: Response; usedFallback: boolean; message: string }> {
  if (response.ok) {
    return { response, usedFallback: false, message: "" };
  }

  const text = await response.text();
  const parsed = parseObject(text);
  const parsedError = parsed?.error;
  const usedFallback = typeof parsedError !== "string" || !parsedError.trim();
  const message = usedFallback ? statusFallback(response.status) : parsedError;
  const payload: ApiErrorPayload = { ...(parsed ?? {}), error: message };
  const headers = new Headers(response.headers);
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.delete("Content-Length");
  headers.delete("Content-Encoding");

  return {
    response: new Response(JSON.stringify(payload), {
      status: response.status,
      statusText: response.statusText,
      headers,
    }),
    usedFallback,
    message,
  };
}
