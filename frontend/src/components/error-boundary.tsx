import { Component, type ReactNode } from "react";

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false, error: null };

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex items-center justify-center min-h-screen">
          <div className="text-center max-w-md mx-auto p-8">
            <h1 className="text-xl font-semibold text-zinc-900 dark:text-zinc-100 mb-2">
              Something went wrong
            </h1>
            <p className="text-sm text-zinc-500 mb-4">
              {this.state.error?.message || "An unexpected error occurred."}
            </p>
            <button
              onClick={() => window.location.reload()}
              className="px-4 py-2 text-sm bg-zinc-900 dark:bg-zinc-100 text-white dark:text-zinc-900 rounded-lg hover:opacity-90"
            >
              Reload page
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
