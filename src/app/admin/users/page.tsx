"use client";

import { useState, useEffect } from "react";
import { Users, Search, Pencil, Trash2, ChevronLeft, ChevronRight, Plus, X } from "lucide-react";
import { cn } from "@/lib/utils";

interface User {
  id: string;
  name: string;
  email: string;
  role: "admin" | "editor" | "viewer";
  avatar: string | null;
  createdAt: string;
  updatedAt: string;
}

interface Pagination {
  page: number;
  limit: number;
  total: number;
  pages: number;
}

export default function AdminUsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [pagination, setPagination] = useState<Pagination>({ page: 1, limit: 50, total: 0, pages: 0 });
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [showAddForm, setShowAddForm] = useState(false);
  const [editingUser, setEditingUser] = useState<User | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  // Add form state
  const [addName, setAddName] = useState("");
  const [addEmail, setAddEmail] = useState("");
  const [addPassword, setAddPassword] = useState("");
  const [addRole, setAddRole] = useState<string>("viewer");

  // Edit form state
  const [editName, setEditName] = useState("");
  const [editEmail, setEditEmail] = useState("");
  const [editPassword, setEditPassword] = useState("");
  const [editRole, setEditRole] = useState<string>("");

  const loadUsers = async (page = 1, q = "") => {
    setLoading(true);
    const params = new URLSearchParams({ page: String(page), limit: "50" });
    if (q) params.set("q", q);

    const res = await fetch(`/api/admin/users?${params}`);
    if (res.ok) {
      const data = await res.json();
      setUsers(data.users);
      setPagination(data.pagination);
    } else if (res.status === 403) {
      setError("You don't have permission to manage users");
    }
    setLoading(false);
  };

  useEffect(() => { loadUsers(); }, []);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    loadUsers(1, search);
  };

  const handleAddUser = async () => {
    if (!addName.trim() || !addEmail.trim() || !addPassword) {
      setError("Name, email, and password are required");
      return;
    }
    setError("");

    const res = await fetch("/api/admin/users", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: addName.trim(),
        email: addEmail.trim(),
        password: addPassword,
        role: addRole,
      }),
    });

    if (res.ok) {
      const created = await res.json();
      setUsers([created, ...users]);
      setPagination((p) => ({ ...p, total: p.total + 1 }));
      setShowAddForm(false);
      setAddName("");
      setAddEmail("");
      setAddPassword("");
      setAddRole("viewer");
      setSuccess(`Created user ${created.name} successfully`);
      setTimeout(() => setSuccess(""), 3000);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to create user");
    }
  };

  const handleEditStart = (user: User) => {
    setEditingUser(user);
    setEditRole(user.role);
    setEditName(user.name);
    setEditEmail(user.email);
    setEditPassword("");
    setError("");
    setSuccess("");
  };

  const handleEditSave = async () => {
    if (!editingUser) return;
    setError("");

    const payload: Record<string, string | undefined> = {
      userId: editingUser.id,
    };
    if (editName !== editingUser.name) payload.name = editName;
    if (editEmail !== editingUser.email) payload.email = editEmail;
    if (editRole !== editingUser.role) payload.role = editRole;
    if (editPassword) payload.password = editPassword;

    const res = await fetch("/api/admin/users", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (res.ok) {
      const updated = await res.json();
      setUsers(users.map((u) => (u.id === updated.id ? { ...u, ...updated } : u)));
      setEditingUser(null);
      setSuccess(`Updated ${updated.name} successfully`);
      setTimeout(() => setSuccess(""), 3000);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to update user");
    }
  };

  const handleDelete = async (user: User) => {
    if (!confirm(`Are you sure you want to delete "${user.name}"? This action cannot be undone.`)) {
      return;
    }

    const res = await fetch(`/api/admin/users?id=${user.id}`, { method: "DELETE" });
    if (res.ok) {
      setUsers(users.filter((u) => u.id !== user.id));
      setSuccess(`Deleted ${user.name}`);
      setTimeout(() => setSuccess(""), 3000);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to delete user");
    }
  };

  const roleBadgeColor = (role: string) => {
    switch (role) {
      case "admin": return "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300";
      case "editor": return "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300";
      default: return "bg-zinc-100 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300";
    }
  };

  if (loading && users.length === 0) {
    return <div className="text-center py-12 text-zinc-500">Loading users...</div>;
  }

  return (
    <div className="max-w-5xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Users size={24} className="text-zinc-400" />
          <div>
            <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">User Management</h1>
            <p className="text-sm text-zinc-500">{pagination.total} users total</p>
          </div>
        </div>
        <button
          onClick={() => { setShowAddForm(true); setEditingUser(null); }}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
        >
          <Plus size={16} />
          Add User
        </button>
      </div>

      {/* Messages */}
      {error && (
        <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">
          {error}
        </div>
      )}
      {success && (
        <div className="mb-4 p-3 bg-green-50 dark:bg-green-950 border border-green-200 dark:border-green-800 rounded-lg text-sm text-green-600 dark:text-green-400">
          {success}
        </div>
      )}

      {/* Search */}
      <form onSubmit={handleSearch} className="mb-6">
        <div className="relative">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-zinc-400" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name or email..."
            className="w-full pl-9 pr-4 py-2 text-sm bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
      </form>

      {/* Add User Form */}
      {showAddForm && (
        <div className="mb-6 p-5 border border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-950 rounded-xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-medium text-green-900 dark:text-green-100">Add New User</h3>
            <button onClick={() => setShowAddForm(false)} className="text-green-500 hover:text-green-700">
              <X size={16} />
            </button>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Name *</label>
              <input
                type="text"
                value={addName}
                onChange={(e) => setAddName(e.target.value)}
                placeholder="Full name"
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Email *</label>
              <input
                type="email"
                value={addEmail}
                onChange={(e) => setAddEmail(e.target.value)}
                placeholder="user@example.com"
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Password *</label>
              <input
                type="password"
                value={addPassword}
                onChange={(e) => setAddPassword(e.target.value)}
                placeholder="Min 6 characters"
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Role</label>
              <select
                value={addRole}
                onChange={(e) => setAddRole(e.target.value)}
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              >
                <option value="viewer">Viewer</option>
                <option value="editor">Editor</option>
                <option value="admin">Admin</option>
              </select>
            </div>
          </div>
          <div className="flex gap-2 mt-4">
            <button onClick={handleAddUser} className="px-4 py-1.5 bg-green-600 hover:bg-green-700 text-white text-sm rounded-lg">
              Create User
            </button>
            <button onClick={() => setShowAddForm(false)} className="px-4 py-1.5 text-sm text-zinc-500 hover:text-zinc-700">
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Edit User Form */}
      {editingUser && (
        <div className="mb-6 p-5 border border-blue-200 dark:border-blue-800 bg-blue-50 dark:bg-blue-950 rounded-xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-medium text-blue-900 dark:text-blue-100">
              Edit User: {editingUser.email}
            </h3>
            <button onClick={() => setEditingUser(null)} className="text-blue-500 hover:text-blue-700">
              <X size={16} />
            </button>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Name</label>
              <input
                type="text"
                value={editName}
                onChange={(e) => setEditName(e.target.value)}
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Email</label>
              <input
                type="email"
                value={editEmail}
                onChange={(e) => setEditEmail(e.target.value)}
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">New Password (leave empty to keep current)</label>
              <input
                type="password"
                value={editPassword}
                onChange={(e) => setEditPassword(e.target.value)}
                placeholder="••••••••"
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Role</label>
              <select
                value={editRole}
                onChange={(e) => setEditRole(e.target.value)}
                className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              >
                <option value="admin">Admin</option>
                <option value="editor">Editor</option>
                <option value="viewer">Viewer</option>
              </select>
            </div>
          </div>
          <div className="flex gap-2 mt-4">
            <button onClick={handleEditSave} className="px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg">
              Save Changes
            </button>
            <button onClick={() => setEditingUser(null)} className="px-4 py-1.5 text-sm text-zinc-500 hover:text-zinc-700">
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Users Table */}
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
              <th className="text-left px-4 py-3 font-medium text-zinc-500">User</th>
              <th className="text-left px-4 py-3 font-medium text-zinc-500">Role</th>
              <th className="text-left px-4 py-3 font-medium text-zinc-500">Joined</th>
              <th className="text-right px-4 py-3 font-medium text-zinc-500">Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.id} className="border-b border-zinc-100 dark:border-zinc-800 last:border-0">
                <td className="px-4 py-3">
                  <div className="flex items-center gap-3">
                    <div className="w-8 h-8 rounded-full bg-zinc-200 dark:bg-zinc-700 flex items-center justify-center text-xs font-medium text-zinc-600 dark:text-zinc-300">
                      {user.name.charAt(0).toUpperCase()}
                    </div>
                    <div>
                      <p className="font-medium text-zinc-900 dark:text-zinc-100">{user.name}</p>
                      <p className="text-xs text-zinc-400">{user.email}</p>
                    </div>
                  </div>
                </td>
                <td className="px-4 py-3">
                  <span className={cn("px-2 py-0.5 rounded-full text-xs font-medium", roleBadgeColor(user.role))}>
                    {user.role}
                  </span>
                </td>
                <td className="px-4 py-3 text-zinc-500">
                  {new Date(user.createdAt).toLocaleDateString()}
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center justify-end gap-1">
                    <button
                      onClick={() => handleEditStart(user)}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-400 hover:text-blue-600"
                      title="Edit user"
                    >
                      <Pencil size={14} />
                    </button>
                    <button
                      onClick={() => handleDelete(user)}
                      className="p-1.5 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 text-zinc-400 hover:text-red-600"
                      title="Delete user"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {pagination.pages > 1 && (
        <div className="flex items-center justify-between mt-4">
          <p className="text-xs text-zinc-500">
            Page {pagination.page} of {pagination.pages}
          </p>
          <div className="flex items-center gap-1">
            <button
              onClick={() => loadUsers(pagination.page - 1, search)}
              disabled={pagination.page <= 1}
              className="p-1.5 rounded-lg border border-zinc-300 dark:border-zinc-700 disabled:opacity-30 hover:bg-zinc-50 dark:hover:bg-zinc-800"
            >
              <ChevronLeft size={14} />
            </button>
            <button
              onClick={() => loadUsers(pagination.page + 1, search)}
              disabled={pagination.page >= pagination.pages}
              className="p-1.5 rounded-lg border border-zinc-300 dark:border-zinc-700 disabled:opacity-30 hover:bg-zinc-50 dark:hover:bg-zinc-800"
            >
              <ChevronRight size={14} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
