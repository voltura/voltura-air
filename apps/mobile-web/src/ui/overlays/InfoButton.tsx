import { useRef, useState } from "react";
import { Info } from "lucide-react";
import { InfoDialog } from "./InfoDialog";

interface InfoButtonProps {
  description: string;
  isOpen?: boolean | undefined;
  onOpenChange?: ((isOpen: boolean) => void) | undefined;
  size?: "compact" | "detailed";
  title: string;
}

export function InfoButton({ description, isOpen: controlledIsOpen, onOpenChange, size = "compact", title }: InfoButtonProps) {
  const [uncontrolledIsOpen, setUncontrolledIsOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement | null>(null);
  const openedWithPointerRef = useRef(false);
  const isOpen = controlledIsOpen ?? uncontrolledIsOpen;
  const setIsOpen = onOpenChange ?? setUncontrolledIsOpen;

  const finishClosing = () => {
    setIsOpen(false);
    if (!openedWithPointerRef.current) {
      buttonRef.current?.focus();
    }
  };

  return (
    <>
      <button
        ref={buttonRef}
        className="info-button"
        type="button"
        aria-label={`About ${title}`}
        onPointerDown={() => { openedWithPointerRef.current = true; }}
        onKeyDown={() => { openedWithPointerRef.current = false; }}
        onClick={() => { setIsOpen(true); }}
      >
        <Info aria-hidden="true" />
      </button>
      <InfoDialog description={description} isOpen={isOpen} onClose={finishClosing} size={size} title={title} />
    </>
  );
}
