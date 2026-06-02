import { Link, useLocation, useNavigate } from "react-router-dom";
import {
  BookOpen,
  Search,
  BarChart3,
  Users,
  PlusCircle,
  Home,
  Shield,
  ChevronDown,
  ChevronRight,
  Key,
  User,
  LogOut,
} from "lucide-react";
import { cn } from "../../lib/utils";
import { useState } from "react";
import { useAuth } from "../../contexts/AuthContext";

interface NavItem {
  label: string;
  href: string;
  icon: React.ReactNode;
  children?: NavItem[];
}

const navigation: NavItem[] = [
  { label: "Home", href: "/", icon: <Home size={18} /> },
  {
    label: "Articles",
    href: "/articles",
    icon: <BookOpen size={18} />,
    children: [
      {
        label: "New Article",
        href: "/articles/new",
        icon: <PlusCircle size={16} />,
      },
    ],
  },
  { label: "Search", href: "/search", icon: <Search size={18} /> },
  { label: "Analytics", href: "/analytics", icon: <BarChart3 size={18} /> },
];

const adminNavigation: NavItem[] = [
  { label: "Users", href: "/admin/users", icon: <Users size={18} /> },
  { label: "API Keys", href: "/settings/keys", icon: <Key size={18} /> },
];

function NavLink({ item, depth = 0 }: { item: NavItem; depth?: number }) {
  const { pathname } = useLocation();
  const isActive =
    pathname === item.href ||
    (item.href !== "/" && pathname.startsWith(item.href));
  const [expanded, setExpanded] = useState(isActive);

  return (
    <div>
      <div className="flex items-center">
        <Link
          to={item.href}
          className={cn(
            "flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors flex-1",
            isActive
              ? "bg-zinc-100 text-zinc-900 font-medium dark:bg-zinc-800 dark:text-zinc-100"
              : "text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100",
            depth > 0 && "ml-4 text-xs"
          )}
        >
          {item.icon}
          {item.label}
        </Link>
        {item.children && (
          <button
            onClick={() => setExpanded(!expanded)}
            className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            {expanded ? (
              <ChevronDown size={14} />
            ) : (
              <ChevronRight size={14} />
            )}
          </button>
        )}
      </div>
      {item.children && expanded && (
        <div className="mt-1">
          {item.children.map((child) => (
            <NavLink key={child.href} item={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  );
}

export function Sidebar() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const role = user?.role;
  const isAdmin = role === "admin";
  const isEditorOrAdmin = role === "admin" || role === "editor";

  return (
    <aside className="hidden lg:flex lg:flex-col lg:w-64 lg:border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 h-screen sticky top-0">
      {/* Logo */}
      <div className="flex items-center gap-2 px-6 py-5 border-b border-zinc-200 dark:border-zinc-800">
        <Shield size={24} className="text-blue-600" />
        <span className="font-bold text-lg">Knowledge Portal</span>
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">
        <div className="space-y-1">
          {navigation.map((item) => (
            <NavLink key={item.href} item={item} />
          ))}
        </div>

        {/* Admin Section */}
        {isEditorOrAdmin && (
          <div className="pt-6">
            <p className="px-3 text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
              Admin
            </p>
            <div className="space-y-1">
              {adminNavigation
                .filter((item) => {
                  if (item.href === "/admin/users") return isAdmin;
                  if (item.href === "/settings/keys") return isAdmin;
                  return true;
                })
                .map((item) => (
                  <NavLink key={item.href} item={item} />
                ))}
            </div>
          </div>
        )}
      </nav>

      {/* Footer */}
      <div className="px-3 py-3 border-t border-zinc-200 dark:border-zinc-800 space-y-1">
        <Link
          to="/profile"
          className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100 transition-colors"
        >
          <User size={18} />
          <span className="truncate">{user?.name ?? "Profile"}</span>
        </Link>
        <button
          onClick={() => { logout(); navigate("/login"); }}
          className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-zinc-600 hover:bg-zinc-50 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800/50 dark:hover:text-zinc-100 transition-colors w-full"
        >
          <LogOut size={18} />
          Sign out
        </button>
      </div>
    </aside>
  );
}
