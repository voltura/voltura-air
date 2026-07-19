import type { ReactNode } from "react";

export type AppToastTone = "pending" | "success" | "error";

export interface AppToastMessage {
  message: string;
  tone: AppToastTone;
}

interface AppToastProps {
  children: ReactNode;
  tone: AppToastTone;
}

export function AppToast({ children, tone }: AppToastProps) {
  return (
    <div className={`app-toast ${tone}`} role={tone === "error" ? "alert" : "status"}>
      {children}
    </div>
  );
}
