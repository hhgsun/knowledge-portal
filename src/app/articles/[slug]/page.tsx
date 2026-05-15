"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Edit, Clock, User, Tag, Key, ThumbsUp, ThumbsDown } from "lucide-react";
import { TiptapRenderer } from "@/components/editor/tiptap-renderer";

interface Article {
  id: string;
  title: string;
  slug: string;
  content: Record<string, unknown> | null;
  excerpt: string | null;
  status: string;
  contentType: string;
  difficulty: string;
  ownerId: string;
  updatedAt: string;
  publishedAt: string | null;
  lastReviewedAt: string | null;
  ownerName: string | null;
  apiKeyName: string | null;
}

export default function ArticleViewPage() {
  const params = useParams();
  const [article, setArticle] = useState<Article | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (params.slug) {
      fetch(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setArticle(null);
          } else {
            setArticle(data);
          }
          setLoading(false);
        })
        .catch(() => setLoading(false));
    }
  }, [params.slug]);

  const handleFeedback = async (helpful: boolean) => {
    if (!article) return;
    await fetch(`/api/articles/${article.id}/feedback`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ helpful }),
    });
  };

  if (loading) {
    return <div className="text-center py-12 text-zinc-500">Loading...</div>;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">Article not found</p>
        <Link href="/articles" className="text-blue-600 hover:underline text-sm mt-2 inline-block">
          Back to articles
        </Link>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      {/* Breadcrumb */}
      <div className="flex items-center gap-2 mb-6">
        <Link
          href="/articles"
          className="flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-700"
        >
          <ArrowLeft size={14} />
          Articles
        </Link>
        <span className="text-zinc-300">/</span>
        <span className="text-sm text-zinc-700 dark:text-zinc-300">{article.title}</span>
      </div>

      {/* Article Header */}
      <div className="mb-6">
        <div className="flex items-start justify-between">
          <h1 className="text-3xl font-bold text-zinc-900 dark:text-zinc-100">
            {article.title}
          </h1>
          <Link
            href={`/articles/${article.slug}/edit`}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-50 dark:hover:bg-zinc-800"
          >
            <Edit size={14} />
            Edit
          </Link>
        </div>
        {article.excerpt && (
          <p className="text-zinc-500 mt-2">{article.excerpt}</p>
        )}
        <div className="flex items-center gap-4 mt-4 text-sm text-zinc-500">
          <span className="flex items-center gap-1">
            <Clock size={14} />
            {new Date(article.updatedAt).toLocaleDateString()}
          </span>
          <span className="flex items-center gap-1">
            <Tag size={14} />
            {article.contentType}
          </span>
          <span className={`px-2 py-0.5 rounded-full text-xs ${
            article.difficulty === "beginner" ? "bg-blue-100 text-blue-700" :
            article.difficulty === "intermediate" ? "bg-orange-100 text-orange-700" :
            "bg-red-100 text-red-700"
          }`}>
            {article.difficulty}
          </span>
          {article.apiKeyName ? (
            <span className="flex items-center gap-1 text-purple-600 dark:text-purple-400">
              <Key size={14} />
              {article.apiKeyName}
            </span>
          ) : article.ownerName ? (
            <span className="flex items-center gap-1">
              <User size={14} />
              {article.ownerName}
            </span>
          ) : null}
        </div>
      </div>

      {/* Article Content */}
      <div className="prose dark:prose-invert max-w-none border-t border-zinc-200 dark:border-zinc-800 pt-6">
        {article.content ? (
          <TiptapRenderer content={article.content} />
        ) : (
          <p className="text-zinc-400 italic">No content yet.</p>
        )}
      </div>

      {/* Feedback */}
      <div className="mt-8 pt-6 border-t border-zinc-200 dark:border-zinc-800">
        <p className="text-sm text-zinc-500 mb-3">Was this article helpful?</p>
        <div className="flex items-center gap-2">
          <button
            onClick={() => handleFeedback(true)}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-green-50 dark:hover:bg-green-950 hover:border-green-300"
          >
            <ThumbsUp size={14} />
            Yes
          </button>
          <button
            onClick={() => handleFeedback(false)}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 hover:border-red-300"
          >
            <ThumbsDown size={14} />
            No
          </button>
        </div>
      </div>
    </div>
  );
}
