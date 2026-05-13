"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  BookOpen,
  Search,
  BarChart3,
  Settings,
  Users,
  FolderTree,
  PlusCircle,
  Home,
  Shield,
  ChevronDown,
  ChevronRight,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useState } from "react";

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
  {
    label: "Categories",
    href: "/admin/categories",
    icon: <FolderTree size={18} />,
  },
  { label: "Settings", href: "/admin/settings", icon: <Settings size={18} /> },
];

function NavLink({ item, depth = 0 }: { item: NavItem; depth?: number }) {
  const pathname = usePathname();
  const isActive =
    pathname === item.href ||
    (item.href !== "/" && pathname.startsWith(item.href));
  const [expanded, setExpanded] = useState(isActive);

  return (
    <div>
      <div className="flex items-center">
        <Link
          href={item.href}
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
        <div className="pt-6">
          <p className="px-3 text-xs font-semibold text-zinc-400 uppercase tracking-wider mb-2">
            Admin
          </p>
          <div className="space-y-1">
            {adminNavigation.map((item) => (
              <NavLink key={item.href} item={item} />
            ))}
          </div>
        </div>
      </nav>

      {/* Footer */}
      <div className="px-4 py-3 border-t border-zinc-200 dark:border-zinc-800 text-xs text-zinc-500">
        Knowledge Portal v0.1.0
      </div>
    </aside>
  );
}
