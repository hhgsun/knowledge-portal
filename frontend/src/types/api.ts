// Shared API response types — Single Source of Truth for frontend TypeScript

// ─── Enums ───────────────────────────────────────────────────
export type ArticleStatus = "draft" | "published" | "archived";
export type ContentType = string;
export type UserRole = "admin" | "editor" | "viewer";

// ─── Lookups ─────────────────────────────────────────────────
export interface LookupValue {
  id: string;
  category: string;
  value: string;
  label: string;
  color?: string;
  icon?: string;
  sortOrder: number;
  authorityWeight: number;
  isActive: boolean;
}

export interface LookupCategory {
  id: string;
  key: string;
  label: string;
  cardinality: "single" | "multiple";
  isRequired: boolean;
  defaultValueId: string | null;
  ragBehavior: "none" | "filter";
  sortOrder: number;
  isActive: boolean;
}

export interface FeaturedLink {
  id: string;
  label: string;
  linkType: "content_type" | "tag" | "custom";
  target: string;
  icon?: string;
  color?: string;
  sortOrder: number;
  isActive: boolean;
}

// ─── Auth ────────────────────────────────────────────────────
export interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  isAzureUser?: boolean;
  preferredLlmModel?: string | null;
  createdAt?: string;
  updatedAt?: string;
}

export interface AdminUser {
  id: string;
  name: string;
  slug: string;
  email: string;
  role: UserRole;
  isAzureUser: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface LoginResponse {
  token: string;
  user: User;
}

export interface AzureLoginRequest {
  accessToken: string;
}

export interface RegisterResponse {
  id: string;
  name: string;
  email: string;
}

// ─── Articles ────────────────────────────────────────────────
export interface Tag {
  id: string;
  name: string;
  slug: string;
}

export interface TagWithCount extends Tag {
  articleCount: number;
}

export interface LlmModelSettings {
  models: { id: string; label: string }[];
  defaultModel: string;
  preferredModel: string | null;
  effectiveModel: string;
  catalogSource: "ollama" | "stale_cache" | "configured_fallback";
  catalogWarning: string | null;
}

export type ArticleIndexState = "indexed" | "indexing" | "pending" | "stale" | "failed" | "not_applicable";

export interface ArticleIndexingStatus {
  state: ArticleIndexState;
  indexedAt: string | null;
}

export interface ArticleListItem {
  id: string;
  title: string;
  slug: string;
  excerpt: string | null;
  status: ArticleStatus;
  contentType: ContentType;
  updatedAt: string;
  ownerName: string;
  apiKeyName: string | null;
  tags: Tag[];
  viewCount: number;
  wilsonScore: number;
  indexingStatus?: ArticleIndexingStatus | null;
  classifications?: Record<string, string[]>;
}

export interface ArticlesResponse {
  articles: ArticleListItem[];
  total: number;
}

export interface Article {
  id: string;
  title: string;
  slug: string;
  contentMarkdown: string | null;
  contentText: string | null;
  excerpt: string | null;
  status: ArticleStatus;
  contentType: ContentType;
  ownerName: string;
  ownerId: string;
  readTimeMinutes: number | null;
  publishedAt: string | null;
  lastReviewedAt: string | null;
  reviewIntervalDays: number;
  approvedAt: string | null;
  approvedBy: string | null;
  updatedAt: string;
  tags: Tag[];
  apiKeyName: string | null;
  viewCount: number;
  attachments: ArticleAttachment[];
  indexingStatus?: ArticleIndexingStatus | null;
  classifications?: Record<string, string[]>;
}

// ─── Article Versions ────────────────────────────────────────
export interface ArticleVersionListItem {
  id: string;
  version: number;
  title: string;
  changeSummary: string | null;
  changedBy: string;
  changedByName: string | null;
  createdAt: string;
}

export interface ArticleVersionDetail {
  id: string;
  version: number;
  title: string;
  changeSummary: string | null;
  changedBy: string;
  contentMarkdown: string | null;
  createdAt: string;
}

// ─── İlgili Makaleler ────────────────────────────────────────
export interface RelatedArticle {
  id: string;
  title: string;
  slug: string;
  excerpt: string | null;
  contentType: ContentType;
  updatedAt: string;
  tags: Tag[];
}

// ─── Article Votes & Comments ────────────────────────────────
export interface VoteSummary {
  helpful: number;
  notHelpful: number;
  wilsonScore: number;
  userVote: boolean | null;
  reasons: string[];
}

export interface ArticleCommentItem {
  id: string;
  comment: string;
  userName: string;
  createdAt: string;
  isOwn: boolean;
}

// ─── Search ──────────────────────────────────────────────────
export interface SearchResult {
  id: string;
  title: string;
  slug: string;
  excerpt: string | null;
  contentType: ContentType;
  updatedAt: string;
  status: ArticleStatus | null;
  ownerName: string | null;
  apiKeyName: string | null;
  tags: Tag[] | null;
  viewCount: number;
  wilsonScore: number;
  score?: number;
  matchType?: "fulltext" | "semantic" | "both";
  /** Match-context window from the article body; null → fall back to excerpt */
  snippet?: string | null;
}

export interface SearchIndexCoverage {
  mode: "fulltext" | "semantic" | "hybrid";
  fullTextPending: number;
  semanticPending: number;
  relevantPending: number;
}

export interface SearchResponse {
  results: SearchResult[];
  /** True post-filter match count (fulltext/tag); returned count for semantic/hybrid */
  total: number;
  page?: number;
  totalPages?: number;
  searchQueryId: string;
  responseTimeMs: number;
  tags?: string[];
  indexingPending?: boolean;
  indexCoverage?: SearchIndexCoverage;
  warning?: string;
}

export interface RagSource {
  articleId: string;
  title: string;
  slug: string;
  score: number;
  authorityWeight: number;
  approved: boolean;
  reviewState: string;
  reliabilityScore: number;
  updatedAt: string;
}

export interface AssistantEvidence {
  sourceId: string; articleId: string; title: string; slug: string; sourceType: string;
  attachmentId?: string | null; sourceName?: string | null; sourceLocation?: string | null;
  passage: string; score: number; chunkId?: string | null; canonicalUrl?: string | null;
  pageNumber?: number | null;
}

// ─── Grounded Knowledge Assistant ───────────────────────────
export interface AssistantCapabilities {
  enabled: boolean;
  groundedRagEnabled: boolean;
  feedbackEnabled: boolean;
  maxMessageCharacters: number;
  streamingEnabled: boolean;
  conversationHistoryEnabled: boolean;
  semanticCacheEnabled: boolean;
}

export interface AssistantResponse {
  normalizedQuery: string;
  answer: string | null;
  rag: {
    sources: RagSource[];
    consultedSources: RagSource[];
    claims: { text: string; role: "summary" | "explanation" | "step" | "constraint" | "exception" | "conflict"; sourceIds: string[] }[];
    evidence: AssistantEvidence[];
    citationCoverage: number;
    claimSupportCoverage: number;
    groundingStatus: string;
    insufficientContext: boolean;
    partialResult: boolean;
  } | null;
  toolCalls: string[];
  warnings: string[];
  interactionId: string | null;
  responseTimeMs: number;
  traceId: string;
  conversationId?: string | null;
  cacheHit: boolean;
  model?: string | null;
  tokenUsage: {
    inputTokens: number;
    outputTokens: number;
    totalTokens: number;
    estimated: boolean;
  };
}

export interface AssistantConversation {
  id: string; title: string; createdAt: string; updatedAt: string; messageCount: number;
}
export interface AssistantConversationMessage {
  id: string; role: "user" | "assistant"; content: string;
  interactionId?: string | null; createdAt: string;
}

// ─── Dashboard ───────────────────────────────────────────────
export interface DashboardResponse {
  totalArticles: number;
  viewsThisWeek: number;
  searchesToday: number;
  staleCount: number;
  recentArticles: { id: string; title: string; slug: string; contentType: ContentType }[];
  topSearches: { query: string; count: number }[];
}

// ─── Analytics ───────────────────────────────────────────────
export interface AnalyticsOverview {
  totalArticles: number;
  articlesByStatus: Record<string, number>;
  viewsThisWeek: number;
  searchesToday: number;
  staleArticles: number;
}

export interface AnalyticsResponse {
  overview: AnalyticsOverview;
  topSearches: { query: string; count: number }[];
  failedSearches: { query: string; count: number }[];
  topArticles: { articleId: string; title: string; slug: string; views: number }[];
  usage: {
    periodDays: number;
    periodStart: string;
    periodEnd: string;
    totalRequests: number;
    successfulRequests: number;
    errors: number;
    errorRate: number;
    averageDurationMs: number;
    activeUsers: number;
    activeIntegrations: number;
    sessionRequests: number;
    integrationRequests: number;
    restRequests: number;
    mcpCalls: number;
    daily: AnalyticsDailyUsage[];
    users: AnalyticsUserUsage[];
    integrations: AnalyticsIntegrationUsage[];
    operations: AnalyticsOperationUsage[];
  };
}

export interface AnalyticsDailyUsage {
  date: string;
  requests: number;
  errors: number;
  averageDurationMs: number;
  activeUsers: number;
  activeIntegrations: number;
  sessionRequests: number;
  integrationRequests: number;
  restRequests: number;
  mcpCalls: number;
}

export interface AnalyticsUserUsage {
  userId: string;
  name: string;
  email: string;
  role: string;
  requests: number;
  sessionRequests: number;
  integrationRequests: number;
  restRequests: number;
  mcpCalls: number;
  readRequests: number;
  writeRequests: number;
  errors: number;
  errorRate: number;
  averageDurationMs: number;
  lastUsedAt: string;
  activeDays: number;
  integrationsUsed: number;
  topOperation: string | null;
  topOperationRequests: number;
}

export interface AnalyticsIntegrationUsage {
  apiKeyId: string;
  name: string;
  ownerId: string;
  ownerName: string;
  ownerEmail: string;
  requests: number;
  restRequests: number;
  mcpCalls: number;
  readRequests: number;
  writeRequests: number;
  errors: number;
  errorRate: number;
  averageDurationMs: number;
  lastUsedAt: string;
  activeDays: number;
  topOperation: string | null;
  topOperationRequests: number;
}

export interface AnalyticsOperationUsage {
  operation: string;
  channel: string;
  requests: number;
  errors: number;
  errorRate: number;
  averageDurationMs: number;
  lastUsedAt: string;
  uniqueUsers: number;
  uniqueIntegrations: number;
}

// ─── API Keys ────────────────────────────────────────────────
export interface ApiKey {
  id: string;
  name: string;
  lastUsedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}

export interface CreateApiKeyResponse extends ApiKey {
  key: string; // Raw key, only returned once
}

// ─── Admin API Keys ──────────────────────────────────────────
export interface AdminApiKey extends ApiKey {
  keyPrefix: string;
  userId: string;
  userName: string;
  userEmail: string;
}

export interface AdminApiKeysResponse {
  keys: AdminApiKey[];
  total: number;
}

// ─── Admin Users ─────────────────────────────────────────────
export interface AdminUsersResponse {
  users: AdminUser[];
  total: number;
}

// ─── Common ──────────────────────────────────────────────────
export interface ApiError {
  error: string;
}

export interface PaginationParams {
  page?: number;
  limit?: number;
}

// ─── Attachments ─────────────────────────────────────────────
export interface ArticleAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  downloadUrl: string;
  includeInIndex: boolean;
  extractionStatus: "pending" | "completed" | "no_text" | "failed";
  extractionTruncated: boolean;
  extractedCharacters: number;
  extractionCharacterLimit: number;
  createdAt: string;
}

export interface AttachmentListResponse {
  attachments: ArticleAttachment[];
  total: number;
}
