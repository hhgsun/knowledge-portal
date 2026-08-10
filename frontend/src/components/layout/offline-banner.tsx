import { WifiOff } from "lucide-react";
import { useNetworkStatus } from "../../hooks/useNetworkStatus";

export function OfflineBanner() {
  const { isOnline } = useNetworkStatus();

  if (isOnline) return null;

  return (
    <div
      role="alert"
      aria-live="assertive"
      className="fixed top-0 left-0 right-0 z-[100] flex items-center justify-center gap-2 py-2 bg-amber-500 text-white text-sm font-medium shadow-md"
    >
      <WifiOff size={16} aria-hidden="true" />
      <span>Çevrimdışısınız. Bazı özellikler kullanılamayabilir.</span>
    </div>
  );
}
