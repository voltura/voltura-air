import { useLayoutEffect, useRef } from "react";
import { ArrowRight, Check } from "lucide-react";
import "./spotlight.css";

interface SpotlightCardProps {
  copy: string;
  index: number;
  count: number;
  paused: boolean;
  onBack: () => void;
  onNext: () => void;
  onPause: () => void;
  onSkip: () => void;
}

// Interactive guidance is separate from the existing noninteractive hints.
export function SpotlightCard(props: SpotlightCardProps) {
  const card = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    card.current
      ?.querySelector<HTMLButtonElement>("[data-guide-next]")
      ?.focus({ preventScroll: true });
  }, []);
  return (
    <div
      ref={card}
      className="spotlight-card"
      role="group"
      aria-label="Introduction controls"
      data-introduction-controls
    >
      <div className="spotlight-heading">
        <div className="spotlight-heading-label">
          <span className="spotlight-kicker">Quick tour</span>
          <span className="spotlight-progress">
            {props.index + 1} / {props.count}
          </span>
        </div>
        <button type="button" className="spotlight-skip" onClick={props.onSkip}>
          Skip tour
        </button>
      </div>
      <p className="spotlight-copy" aria-live="polite" aria-atomic="true">
        {props.copy}
      </p>
      <div className="spotlight-track" aria-hidden="true">
        <span style={{ width: `${((props.index + 1) / props.count) * 100}%` }} />
      </div>
      <div className="spotlight-controls">
        <button type="button" disabled={props.index === 0} onClick={props.onBack}>
          Back
        </button>
        <button type="button" onClick={props.onPause}>
          {props.paused ? "Resume" : "Pause"}
        </button>
        <button type="button" className="spotlight-next" data-guide-next onClick={props.onNext}>
          {props.index === props.count - 1 ? "Done" : "Next"}
          {props.index === props.count - 1 ? (
            <Check aria-hidden="true" />
          ) : (
            <ArrowRight aria-hidden="true" />
          )}
        </button>
      </div>
    </div>
  );
}
