import { Toaster } from "sonner";

export function ToastProvider() {
  return (
    <Toaster
      position="bottom-right"
      toastOptions={{
        className: "text-sm",
        duration: 4000,
      }}
    />
  );
}
