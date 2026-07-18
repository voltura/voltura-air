import { useEffect, useLayoutEffect, useRef, useState, type Dispatch, type SetStateAction, type SyntheticEvent, type TouchEvent } from "react";
import { saveTextSnippets, type SavedTextSnippet } from "../../../foundation/settings/textSnippets";

const snippetLongPressMs = 450;
const snippetDragCancelDistance = 10;
const snippetClickSuppressionMs = 750;

interface SnippetTouchState {
  active: boolean;
  id: string;
  lastY: number;
  scrolling: boolean;
  scrollTarget: HTMLElement | null;
  startX: number;
  startY: number;
  timer: number;
  touchId: number;
}

interface UseSnippetReorderOptions {
  clientId: string;
  setSnippets: Dispatch<SetStateAction<SavedTextSnippet[]>>;
  snippets: SavedTextSnippet[];
}

export function useSnippetReorder({ clientId, setSnippets, snippets }: UseSnippetReorderOptions) {
  const [draggingSnippetId, setDraggingSnippetId] = useState<string | null>(null);
  const [snippetDragOffsetY, setSnippetDragOffsetY] = useState(0);
  const [snippetReorderFeedback, setSnippetReorderFeedback] = useState<string | null>(null);
  const snippetsRef = useRef(snippets);
  const snippetDragRef = useRef<SnippetTouchState | null>(null);
  const suppressSnippetClickUntilRef = useRef(0);
  const snippetDragScrollRef = useRef<{ restore: () => void } | null>(null);

  useEffect(() => {
    snippetsRef.current = snippets;
  }, [snippets]);

  useLayoutEffect(() => {
    if (draggingSnippetId) {
      snippetDragScrollRef.current?.restore();
    }
  }, [draggingSnippetId, snippets]);

  useEffect(() => () => {
    if (snippetDragRef.current) {
      window.clearTimeout(snippetDragRef.current.timer);
    }
  }, []);

  const reorderSnippet = (draggedId: string, targetId: string) => {
    if (draggedId === targetId) {
      return;
    }

    setSnippets((current) => {
      const fromIndex = current.findIndex((snippet) => snippet.id === draggedId);
      const toIndex = current.findIndex((snippet) => snippet.id === targetId);
      if (fromIndex < 0 || toIndex < 0) {
        return current;
      }

      const next = [...current];
      const [dragged] = next.splice(fromIndex, 1);
      if (!dragged) {
        return current;
      }
      next.splice(toIndex, 0, dragged);
      snippetsRef.current = next;
      return next;
    });
  };

  const startSnippetLongPress = (event: TouchEvent<HTMLLIElement>, snippet: SavedTextSnippet) => {
    if (event.touches.length !== 1) {
      return;
    }

    const touch = event.touches[0];
    if (!touch) {
      return;
    }
    const mode = event.currentTarget.closest<HTMLElement>(".text-transfer-mode");
    const snippetsPane = event.currentTarget.closest<HTMLElement>(".saved-snippets");
    const scrollTarget = snippetsPane && snippetsPane.scrollHeight > snippetsPane.clientHeight ? snippetsPane : mode;
    const drag = {
      active: false,
      id: snippet.id,
      lastY: touch.clientY,
      scrolling: false,
      scrollTarget,
      startX: touch.clientX,
      startY: touch.clientY,
      timer: 0,
      touchId: touch.identifier
    };
    drag.timer = window.setTimeout(() => {
      drag.active = true;
      suppressSnippetClickUntilRef.current = Number.POSITIVE_INFINITY;
      if (mode) {
        const currentSnippetsPane = mode.querySelector<HTMLElement>(".saved-snippets");
        const modeTop = mode.scrollTop;
        const snippetsTop = currentSnippetsPane?.scrollTop ?? 0;
        const restore = () => {
          mode.scrollTop = modeTop;
          if (currentSnippetsPane) {
            currentSnippetsPane.scrollTop = snippetsTop;
          }
        };
        snippetDragScrollRef.current = { restore };
      }
      setDraggingSnippetId(snippet.id);
      setSnippetDragOffsetY(0);
      setSnippetReorderFeedback(`Moving ${snippet.name}. Drag up or down, then release.`);
    }, snippetLongPressMs);
    snippetDragRef.current = drag;
  };

  const moveSnippet = (event: TouchEvent<HTMLLIElement>) => {
    const drag = snippetDragRef.current;
    if (!drag) {
      return;
    }

    const touch = Array.from(event.touches).find((candidate) => candidate.identifier === drag.touchId);
    if (!touch) {
      return;
    }

    if (!drag.active) {
      if (!drag.scrolling && Math.hypot(touch.clientX - drag.startX, touch.clientY - drag.startY) > snippetDragCancelDistance) {
        window.clearTimeout(drag.timer);
        drag.scrolling = true;
      }
      if (drag.scrolling) {
        event.preventDefault();
        event.stopPropagation();
        if (drag.scrollTarget) {
          drag.scrollTarget.scrollTop += drag.lastY - touch.clientY;
        }
        drag.lastY = touch.clientY;
      }
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    snippetDragScrollRef.current?.restore();
    setSnippetDragOffsetY(touch.clientY - drag.startY);
    const targetCard = document.elementFromPoint(touch.clientX, touch.clientY)?.closest<HTMLElement>("[data-snippet-id]");
    let targetId = targetCard?.dataset.snippetId;
    if (!targetId) {
      const cards = Array.from(document.querySelectorAll<HTMLElement>(".saved-snippets [data-snippet-id]"));
      const firstCard = cards[0];
      const lastCard = cards[cards.length - 1];
      if (firstCard && touch.clientY < firstCard.getBoundingClientRect().top) {
        targetId = firstCard.dataset.snippetId;
      } else if (lastCard && touch.clientY > lastCard.getBoundingClientRect().bottom) {
        targetId = lastCard.dataset.snippetId;
      }
    }
    if (targetId && targetId !== drag.id) {
      reorderSnippet(drag.id, targetId);
      drag.startY = touch.clientY;
      setSnippetDragOffsetY(0);
    }
  };

  const finishSnippetDrag = (event: TouchEvent<HTMLLIElement>) => {
    const drag = snippetDragRef.current;
    if (!drag) {
      return;
    }

    window.clearTimeout(drag.timer);
    snippetDragRef.current = null;
    if (!drag.active) {
      if (drag.scrolling) {
        event.preventDefault();
        event.stopPropagation();
        suppressSnippetClickUntilRef.current = Date.now() + snippetClickSuppressionMs;
      }
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    saveTextSnippets(clientId, snippetsRef.current);
    const snippet = snippetsRef.current.find((candidate) => candidate.id === drag.id);
    const position = snippetsRef.current.findIndex((candidate) => candidate.id === drag.id) + 1;
    snippetDragScrollRef.current?.restore();
    snippetDragScrollRef.current = null;
    setDraggingSnippetId(null);
    setSnippetDragOffsetY(0);
    setSnippetReorderFeedback(snippet ? `${snippet.name} moved to position ${position}.` : null);
    suppressSnippetClickUntilRef.current = Date.now() + snippetClickSuppressionMs;
  };

  const suppressSnippetClick = (event: SyntheticEvent<HTMLLIElement>) => {
    if (Date.now() < suppressSnippetClickUntilRef.current) {
      event.preventDefault();
      event.stopPropagation();
    }
  };

  return {
    draggingSnippetId,
    finishSnippetDrag,
    moveSnippet,
    snippetDragOffsetY,
    snippetReorderFeedback,
    startSnippetLongPress,
    suppressSnippetClick
  };
}
