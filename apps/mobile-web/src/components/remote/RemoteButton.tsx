import type { ButtonHTMLAttributes, ReactNode } from "react";

export type RepeatablePressProps = Pick<ButtonHTMLAttributes<HTMLButtonElement>, "onPointerDown" | "onPointerMove" | "onPointerUp" | "onPointerCancel" | "onPointerLeave" | "onClick">;

type RemoteButtonProps = {
  label: string;
  onClick?: () => void;
  pressProps?: RepeatablePressProps;
  children?: ReactNode;
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function RemoteButton({ label, onClick, pressProps, children, className, disabled = false, title }: RemoteButtonProps) {
  return (
    <button type="button" aria-label={label} className={className} disabled={disabled} title={title} {...(pressProps ?? { onClick })}>
      {children ?? <span>{label}</span>}
    </button>
  );
}
