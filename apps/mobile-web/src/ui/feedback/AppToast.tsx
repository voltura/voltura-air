import type { ReactNode } from "react";

export type AppToastTone = "pending" | "success" | "error";

export interface AppToastMessage {
  message: string;
  tone: AppToastTone;
}

interface AppToastProps {
  children: ReactNode;
  onDismiss?: () => void;
  tone: AppToastTone;
}

export function AppToast({ children, onDismiss, tone }: AppToastProps) {
  return (
    <div className={`app-toast ${tone}`} role={tone === "error" ? "alert" : "status"}>
      <span>{children}</span>
      {onDismiss && <button type="button" aria-label="Dismiss message" onClick={onDismiss}>×</button>}
    </div>
  );
}
