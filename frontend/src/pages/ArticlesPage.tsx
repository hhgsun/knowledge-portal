import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { PlusCircle, BookOpen, User, Key } from "lucide-react";
import { useApi } from "../hooks/useApi";

interface Article {
  id: string;
  title: string;
  slug: string;
  excerpt: string | null;
  status: string;
  contentType: string;
  difficulty: string;
  updatedAt: string;
  ownerName: string | null;
  apiKeyName: string | null;
}

export default function ArticlesPage() {
  const { fetchWithAuth } = useApi();
  const [articles, setArticles] = useState<Article[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchWithAuth("/api/articles")
      .then((res) => res.json())
      .then((data) => {
        setArticles(data.articles || []);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [fetchWithAuth]);

  const statusColors: Record<string, string> = {
    draft: "bg-zinc-100 text-zinc-600",
    in_review: "bg-yellow-100 text-yellow-700",
    published: "bg-green-100 text-green-700",
    archived: "bg-red-100 text-red-700",
  };

  const difficultyColors: Record<string, string> = {
    beginner: "bg-blue-100 text-blue-700",
    intermediate: "bg-orange-100 text-orange-700",
    advanced: "bg-red-100 text-red-700",
  };

  return (
    <div className="max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Articles</h1>
          <p className="text-sm text-zinc-500 mt-1">Browse and manage knowledge articles</p>
        </div>
        <Link
          to="/articles/new"
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg transition-colors"
        >
          <PlusCircle size={16} />
          New Article
        </Link>
      </div>

      {loading ? (
        <div className="text-center py-12 text-zinc-500">Loading...</div>
      ) : articles.length === 0 ? (
        <div className="text-center py-12 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <BookOpen size={40} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">No articles yet</p>
          <Link to="/articles/new" className="text-blue-600 hover:underline text-sm mt-1 inline-block">
            Create your first article
          </Link>
        </div>
      ) : (
        <div className="space-y-3">
          {articles.map((article) => (
            <Link
              key={article.id}
              to={`/articles/${article.slug}`}
              className="block border border-zinc-200 dark:border-zinc-800 rounded-xl p-4 hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
            >
              <div className="flex items-start justify-between">
                <div className="flex-1">
                  <h3 className="font-medium text-zinc-900 dark:text-zinc-100">{article.title}</h3>
                  {article.excerpt && (
                    <p className="text-sm text-zinc-500 mt-1 line-clamp-2">{article.excerpt}</p>
                  )}
                  <div className="flex items-center gap-2 mt-2">
                    <span className={`text-xs px-2 py-0.5 rounded-full ${statusColors[article.status] || ""}`}>
                      {article.status}
                    </span>
                    <span className={`text-xs px-2 py-0.5 rounded-full ${difficultyColors[article.difficulty] || ""}`}>
                      {article.difficulty}
                    </span>
                    <span className="text-xs text-zinc-400">{article.contentType}</span>
                    {article.apiKeyName ? (
                      <span className="flex items-center gap-1 text-xs text-purple-600 dark:text-purple-400">
                        <Key size={12} />
                        {article.apiKeyName}
                      </span>
                    ) : article.ownerName ? (
                      <span className="flex items-center gap-1 text-xs text-zinc-500">
                        <User size={12} />
                        {article.ownerName}
                      </span>
                    ) : null}
                  </div>
                </div>
                <span className="text-xs text-zinc-400 ml-4 whitespace-nowrap">
                  {new Date(article.updatedAt).toLocaleDateString()}
                </span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
