import { cn } from "../../lib/utils";

interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className }: SkeletonProps) {
  return (
    <div
      aria-hidden="true"
      className={cn("animate-pulse rounded bg-zinc-200 dark:bg-zinc-700", className)}
    />
  );
}

/** Full-page app loading skeleton (ProtectedRoute) */
export function AppLoadingSkeleton() {
  return (
    <div className="flex min-h-screen min-w-screen" aria-label="Loading application">
      {/* Sidebar skeleton */}
      <div className="hidden lg:flex lg:flex-col lg:w-64 border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 p-4 space-y-4">
        <Skeleton className="h-8 w-32" />
        <div className="space-y-2 mt-6">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-full rounded-lg" />
          ))}
        </div>
      </div>
      {/* Main content skeleton */}
      <div className="flex-1 p-6 space-y-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-4 w-72" />
        <div className="mt-8 space-y-3">
          <Skeleton className="h-24 w-full rounded-xl" />
          <Skeleton className="h-24 w-full rounded-xl" />
        </div>
      </div>
    </div>
  );
}

/** Dashboard/Home page skeleton */
export function HomeSkeleton() {
  return (
    <div className="max-w-4xl mx-auto" aria-label="Loading dashboard">
      <div className="flex flex-col items-center text-center pt-8 pb-10">
        <Skeleton className="h-9 w-56 mb-2" />
        <Skeleton className="h-5 w-72 mb-8" />
        <Skeleton className="h-14 w-full max-w-2xl rounded-xl" />
        <div className="flex gap-2 mt-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-7 w-16 rounded-full" />
          ))}
        </div>
      </div>
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
        <Skeleton className="h-6 w-36 mb-4" />
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="flex items-center justify-between">
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/** Article list skeleton */
export function ArticleListSkeleton() {
  return (
    <div className="space-y-3" aria-label="Loading articles">
      {Array.from({ length: 5 }).map((_, i) => (
        <div key={i} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl animate-pulse">
          <div className="flex items-start justify-between">
            <div className="flex-1 space-y-2">
              <Skeleton className="h-5 w-3/4" />
              <Skeleton className="h-4 w-full" />
              <div className="flex items-center gap-2 mt-2">
                <Skeleton className="h-5 w-16 rounded-full" />
                <Skeleton className="h-4 w-12" />
                <Skeleton className="h-4 w-20" />
              </div>
            </div>
            <Skeleton className="h-4 w-20 ml-4" />
          </div>
        </div>
      ))}
    </div>
  );
}

/** Article detail/view skeleton */
export function ArticleViewSkeleton() {
  return (
    <div className="max-w-4xl mx-auto" aria-label="Loading article">
      <Skeleton className="h-5 w-24 mb-4" />
      <Skeleton className="h-9 w-2/3 mb-3" />
      <div className="flex items-center gap-3 mb-6">
        <Skeleton className="h-5 w-20 rounded-full" />
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-4 w-24" />
      </div>
      <div className="space-y-3 mb-8">
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-5/6" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-4/6" />
        <Skeleton className="h-32 w-full rounded-lg mt-4" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-3/4" />
      </div>
    </div>
  );
}

/** Edit article loading skeleton */
export function EditArticleSkeleton() {
  return (
    <div className="max-w-4xl mx-auto" aria-label="Loading editor">
      <div className="flex items-center justify-between mb-6">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-9 w-28 rounded-lg" />
      </div>
      <Skeleton className="h-10 w-full rounded-lg mb-4" />
      <Skeleton className="h-10 w-1/3 rounded-lg mb-4" />
      <Skeleton className="h-64 w-full rounded-xl mb-4" />
      <div className="flex gap-2">
        <Skeleton className="h-8 w-20 rounded-lg" />
        <Skeleton className="h-8 w-20 rounded-lg" />
        <Skeleton className="h-8 w-20 rounded-lg" />
      </div>
    </div>
  );
}

/** Analytics dashboard skeleton */
export function AnalyticsSkeleton() {
  return (
    <div className="max-w-7xl mx-auto" aria-label="Loading analytics">
      <Skeleton className="h-8 w-52 mb-6" />
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-4">
            <Skeleton className="h-5 w-5 mb-2 rounded" />
            <Skeleton className="h-8 w-16 mb-1" />
            <Skeleton className="h-4 w-24" />
          </div>
        ))}
      </div>
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-3 mb-8">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-3">
            <Skeleton className="h-4 w-4 mb-2 rounded" />
            <Skeleton className="h-7 w-14 mb-1" />
            <Skeleton className="h-3 w-20" />
          </div>
        ))}
      </div>
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-5 mb-8">
        <Skeleton className="h-5 w-44 mb-5" />
        <Skeleton className="h-36 w-full" />
      </div>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <Skeleton className="h-6 w-40 mb-4" />
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="flex items-center justify-between">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-4 w-16" />
              </div>
            ))}
          </div>
        </div>
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <Skeleton className="h-6 w-44 mb-4" />
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="flex items-center justify-between">
                <Skeleton className="h-4 w-36" />
                <Skeleton className="h-4 w-12" />
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

/** Tags page skeleton */
export function TagsListSkeleton() {
  return (
    <div className="max-w-3xl mx-auto" aria-label="Loading tags">
      <div className="flex items-center justify-between mb-6">
        <Skeleton className="h-7 w-28" />
        <Skeleton className="h-9 w-24 rounded-lg" />
      </div>
      <div className="space-y-2">
        {Array.from({ length: 8 }).map((_, i) => (
          <div key={i} className="flex items-center justify-between p-3 border border-zinc-200 dark:border-zinc-800 rounded-lg">
            <div className="flex items-center gap-2">
              <Skeleton className="h-5 w-5 rounded" />
              <Skeleton className="h-4 w-24" />
            </div>
            <Skeleton className="h-4 w-16" />
          </div>
        ))}
      </div>
    </div>
  );
}

/** Admin users table skeleton */
export function UsersListSkeleton() {
  return (
    <div className="max-w-3xl mx-auto" aria-label="Loading users">
      <div className="flex items-center justify-between mb-6">
        <Skeleton className="h-7 w-36" />
        <Skeleton className="h-9 w-28 rounded-lg" />
      </div>
      <div className="space-y-2">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="flex items-center gap-3 p-3 border border-zinc-200 dark:border-zinc-800 rounded-lg">
            <Skeleton className="h-8 w-8 rounded-full" />
            <div className="flex-1 space-y-1">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-48" />
            </div>
            <Skeleton className="h-5 w-14 rounded-full" />
          </div>
        ))}
      </div>
    </div>
  );
}

/** API keys list skeleton */
export function KeysListSkeleton() {
  return (
    <div className="max-w-3xl mx-auto" aria-label="Loading API keys">
      <div className="flex items-center justify-between mb-6">
        <Skeleton className="h-7 w-28" />
        <Skeleton className="h-9 w-32 rounded-lg" />
      </div>
      <div className="space-y-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl">
            <div className="flex items-center justify-between mb-2">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <div className="flex items-center gap-4">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-4 w-32" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Lookups page skeleton */
export function LookupsListSkeleton() {
  return (
    <div className="max-w-3xl mx-auto" aria-label="Loading lookups">
      <div className="flex items-center justify-between mb-6">
        <Skeleton className="h-7 w-32" />
        <Skeleton className="h-9 w-24 rounded-lg" />
      </div>
      <div className="space-y-2">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="flex items-center justify-between p-3 border border-zinc-200 dark:border-zinc-800 rounded-lg">
            <div className="flex items-center gap-2">
              <Skeleton className="h-6 w-6 rounded" />
              <Skeleton className="h-4 w-28" />
            </div>
            <div className="flex items-center gap-2">
              <Skeleton className="h-5 w-16 rounded-full" />
              <Skeleton className="h-6 w-6 rounded" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Versions page skeleton */
export function VersionsListSkeleton() {
  return (
    <div className="max-w-4xl mx-auto" aria-label="Loading versions">
      <Skeleton className="h-5 w-24 mb-4" />
      <Skeleton className="h-8 w-64 mb-6" />
      <div className="space-y-3">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl">
            <div className="flex items-center justify-between mb-2">
              <Skeleton className="h-5 w-28" />
              <Skeleton className="h-4 w-32" />
            </div>
            <Skeleton className="h-4 w-3/4" />
          </div>
        ))}
      </div>
    </div>
  );
}
