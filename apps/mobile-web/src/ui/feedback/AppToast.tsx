import type { ReactNode } from "react";

interface AppToastProps {
  children: ReactNode;
  tone: "pending" | "success" | "error";
}

export function AppToast({ children, tone }: AppToastProps) {
  return (
    <div className={`app-toast ${tone}`} role={tone === "error" ? "alert" : "status"}>
      {children}
    </div>
  );
}
