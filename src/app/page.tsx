import { BookOpen, TrendingUp, AlertTriangle, Search } from "lucide-react";

export default function HomePage() {
  return (
    <div className="max-w-6xl mx-auto">
      {/* Welcome */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
          Welcome to Knowledge Portal
        </h1>
        <p className="mt-1 text-zinc-500">
          Your enterprise knowledge base — search, browse, and contribute.
        </p>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <StatCard
          icon={<BookOpen size={20} />}
          label="Total Articles"
          value="—"
          color="blue"
        />
        <StatCard
          icon={<TrendingUp size={20} />}
          label="Views This Week"
          value="—"
          color="green"
        />
        <StatCard
          icon={<AlertTriangle size={20} />}
          label="Stale Articles"
          value="—"
          color="amber"
        />
        <StatCard
          icon={<Search size={20} />}
          label="Searches Today"
          value="—"
          color="purple"
        />
      </div>

      {/* Quick Actions */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4">
            Recent Articles
          </h2>
          <p className="text-sm text-zinc-500">
            No articles yet. Create your first article to get started.
          </p>
        </div>
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4">
            Popular Searches
          </h2>
          <p className="text-sm text-zinc-500">No search data yet.</p>
        </div>
      </div>
    </div>
  );
}

function StatCard({
  icon,
  label,
  value,
  color,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  color: "blue" | "green" | "amber" | "purple";
}) {
  const colorClasses = {
    blue: "bg-blue-50 text-blue-600 dark:bg-blue-950 dark:text-blue-400",
    green: "bg-green-50 text-green-600 dark:bg-green-950 dark:text-green-400",
    amber: "bg-amber-50 text-amber-600 dark:bg-amber-950 dark:text-amber-400",
    purple:
      "bg-purple-50 text-purple-600 dark:bg-purple-950 dark:text-purple-400",
  };

  return (
    <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-4">
      <div className="flex items-center gap-3">
        <div className={`p-2 rounded-lg ${colorClasses[color]}`}>{icon}</div>
        <div>
          <p className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
            {value}
          </p>
          <p className="text-xs text-zinc-500">{label}</p>
        </div>
      </div>
    </div>
  );
}
