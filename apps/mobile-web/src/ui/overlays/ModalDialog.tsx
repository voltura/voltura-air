import { useCallback, useEffect, useId, useRef, type ReactNode, type RefObject } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";

interface ModalDialogProps {
  actionsClassName?: string | undefined;
  actions?: ReactNode | undefined;
  ariaDescribedBy?: string | undefined;
  children: ReactNode;
  className?: string | undefined;
  dismissLabel: string;
  focusDismissAction?: boolean;
  formClassName?: string | undefined;
  initialFocusRef?: RefObject<HTMLElement | null> | undefined;
  isOpen: boolean;
  landscapeSize?: "content" | "wide" | undefined;
  noValidate?: boolean | undefined;
  onClose: () => void;
  onSubmit?: ((event: React.SubmitEvent<HTMLFormElement>) => boolean) | undefined;
  submitClassName?: string | undefined;
  submitLabel?: string | undefined;
  title: string;
  titleAccessory?: ReactNode | undefined;
}

export function ModalDialog({
  actionsClassName,
  actions,
  ariaDescribedBy,
  children,
  className,
  dismissLabel,
  focusDismissAction = false,
  formClassName,
  initialFocusRef,
  isOpen,
  landscapeSize = "content",
  noValidate,
  onClose,
  onSubmit,
  submitClassName,
  submitLabel,
  title,
  titleAccessory
}: ModalDialogProps) {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);
  const dismissButtonRef = useRef<HTMLButtonElement | null>(null);
  const invokingElementRef = useRef<HTMLElement | null>(null);
  const titleId = useId();

  const finishClosing = useCallback(() => {
    invokingElementRef.current?.focus();
    onClose();
  }, [onClose]);

  const closeDialog = useCallback(() => {
    const dialog = dialogRef.current;
    if (!dialog) {
      finishClosing();
      return;
    }

    if (typeof dialog.close === "function") {
      dialog.close();
      return;
    }

    dialog.removeAttribute("open");
    finishClosing();
  }, [finishClosing]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog || dialog.open) {
      return;
    }

    invokingElementRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (typeof dialog.showModal === "function") {
      dialog.showModal();
    } else {
      dialog.setAttribute("open", "");
    }
    (initialFocusRef?.current ?? (focusDismissAction ? dismissButtonRef.current : null) ?? closeButtonRef.current)?.focus();
  }, [focusDismissAction, initialFocusRef, isOpen]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog) {
      return;
    }

    const visualViewport = window.visualViewport;
    const minimumBottomConstraint = Number.parseFloat(
      getComputedStyle(document.documentElement).getPropertyValue("--control-min-height")
    ) || 48;
    let animationFrame = 0;
    let shouldRevealFocus = true;
    const updateViewportVariables = () => {
      animationFrame = 0;
      const width = visualViewport?.width ?? window.innerWidth;
      const height = visualViewport?.height ?? window.innerHeight;
      const left = visualViewport?.offsetLeft ?? 0;
      const top = visualViewport?.offsetTop ?? 0;
      const bottomOffset = Math.max(0, window.innerHeight - top - height);
      dialog.style.setProperty("--modal-visual-viewport-width", `${width}px`);
      dialog.style.setProperty("--modal-visual-viewport-height", `${height}px`);
      dialog.style.setProperty("--modal-visual-viewport-center-x", `${left + width / 2}px`);
      dialog.style.setProperty("--modal-visual-viewport-center-y", `${top + height / 2}px`);
      dialog.style.setProperty("--modal-visual-viewport-bottom-offset", `${bottomOffset}px`);
      dialog.toggleAttribute(
        "data-visual-viewport-bottom-constrained",
        bottomOffset >= minimumBottomConstraint
      );
      if (shouldRevealFocus) {
        shouldRevealFocus = false;
        const focusedElement = document.activeElement;
        if (
          focusedElement instanceof HTMLElement
          && dialog.contains(focusedElement)
          && typeof focusedElement.scrollIntoView === "function"
        ) {
          focusedElement.scrollIntoView({ block: "nearest", inline: "nearest" });
        }
      }
    };
    const scheduleViewportUpdate = (revealFocus: boolean) => {
      shouldRevealFocus ||= revealFocus;
      if (animationFrame === 0) {
        animationFrame = window.requestAnimationFrame(updateViewportVariables);
      }
    };
    const scheduleResizeUpdate = () => { scheduleViewportUpdate(true); };
    const scheduleScrollUpdate = () => { scheduleViewportUpdate(false); };

    updateViewportVariables();
    visualViewport?.addEventListener("resize", scheduleResizeUpdate);
    visualViewport?.addEventListener("scroll", scheduleScrollUpdate);
    window.addEventListener("resize", scheduleResizeUpdate);
    return () => {
      visualViewport?.removeEventListener("resize", scheduleResizeUpdate);
      visualViewport?.removeEventListener("scroll", scheduleScrollUpdate);
      window.removeEventListener("resize", scheduleResizeUpdate);
      if (animationFrame !== 0) {
        window.cancelAnimationFrame(animationFrame);
      }
    };
  }, [isOpen]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!isOpen || !dialog) {
      return;
    }

    const dismissFromBackdrop = (event: MouseEvent) => {
      const bounds = dialog.getBoundingClientRect();
      const outsideSurface = event.clientX < bounds.left
        || event.clientX > bounds.right
        || event.clientY < bounds.top
        || event.clientY > bounds.bottom;
      if (outsideSurface) {
        closeDialog();
      }
    };
    const closeFromEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        closeDialog();
      }
    };

    dialog.addEventListener("click", dismissFromBackdrop);
    dialog.addEventListener("keydown", closeFromEscape);
    return () => {
      dialog.removeEventListener("click", dismissFromBackdrop);
      dialog.removeEventListener("keydown", closeFromEscape);
    };
  }, [closeDialog, isOpen]);

  if (!isOpen) {
    return null;
  }

  return createPortal(
    <dialog
      ref={dialogRef}
      className={`modal-dialog${landscapeSize === "wide" ? " modal-dialog-landscape-wide" : ""}${className ? ` ${className}` : ""}`}
      aria-describedby={ariaDescribedBy}
      aria-labelledby={titleId}
      aria-modal="true"
      onCancel={(event) => {
        event.preventDefault();
        closeDialog();
      }}
      onClose={(event) => {
        if (event.target === event.currentTarget) {
          finishClosing();
        }
      }}
    >
      <header className="modal-dialog-header">
        <h2 id={titleId}>{title}</h2>
        {titleAccessory}
        <button
          ref={closeButtonRef}
          className="modal-dialog-close"
          type="button"
          aria-label={`Close ${title}`}
          onClick={closeDialog}
        >
          <X aria-hidden="true" />
        </button>
      </header>
      {onSubmit ? (
        <form
          className={`modal-dialog-form${formClassName ? ` ${formClassName}` : ""}`}
          noValidate={noValidate}
          onSubmit={(event) => {
            if (onSubmit(event)) {
              closeDialog();
            }
          }}
        >
          <div className="modal-dialog-body">{children}</div>
          <div className={`modal-dialog-actions${actionsClassName ? ` ${actionsClassName}` : ""}`}>
            {actions ?? <>
              <button ref={dismissButtonRef} type="button" onClick={closeDialog}>{dismissLabel}</button>
              {submitLabel && <button className={submitClassName} type="submit">{submitLabel}</button>}
            </>}
          </div>
        </form>
      ) : (
        <>
          <div className="modal-dialog-body">{children}</div>
          <div className={`modal-dialog-actions${actionsClassName ? ` ${actionsClassName}` : ""}`}>
            {actions ?? <button ref={dismissButtonRef} type="button" onClick={closeDialog}>{dismissLabel}</button>}
          </div>
        </>
      )}
    </dialog>,
    document.body
  );
}
