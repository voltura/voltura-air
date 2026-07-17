import { useRef, useState } from "react";
import { Info } from "lucide-react";
import { InfoDialog } from "./InfoDialog";

interface InfoButtonProps {
  description: string;
  size?: "compact" | "detailed";
  title: string;
}

export function InfoButton({ description, size = "compact", title }: InfoButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement | null>(null);

  const finishClosing = () => {
    setIsOpen(false);
    buttonRef.current?.focus();
  };

  return (
    <>
      <button ref={buttonRef} className="info-button" type="button" aria-label={`About ${title}`} onClick={() => { setIsOpen(true); }}>
        <Info aria-hidden="true" />
      </button>
      <InfoDialog description={description} isOpen={isOpen} onClose={finishClosing} size={size} title={title} />
    </>
  );
}
