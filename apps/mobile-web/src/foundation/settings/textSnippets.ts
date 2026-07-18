export const maxSavedSnippets = 20;
export const maxSnippetLength = 4096;
const maxSnippetNameLength = 60;

export interface SavedTextSnippet {
  id: string;
  name: string;
  text: string;
}

export function textSnippetsKey(clientId: string): string {
  return `voltura-air.textSnippets.${clientId}`;
}

export function loadTextSnippets(clientId: string): SavedTextSnippet[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(textSnippetsKey(clientId)) ?? "[]");
    if (!Array.isArray(value)) {
      return [];
    }

    const ids = new Set<string>();
    return (value as unknown[]).flatMap((candidate): SavedTextSnippet[] => {
      if (typeof candidate !== "object" || candidate === null) {
        return [];
      }

      const { id, name, text } = candidate as Partial<SavedTextSnippet>;
      if (typeof id !== "string" || !/^[a-zA-Z0-9-]{1,64}$/.test(id) || ids.has(id) ||
          typeof name !== "string" || name.trim().length < 1 || name.trim().length > maxSnippetNameLength ||
          typeof text !== "string" || text.length < 1 || text.length > maxSnippetLength) {
        return [];
      }

      ids.add(id);
      return [{ id, name: name.trim(), text }];
    }).slice(0, maxSavedSnippets);
  } catch {
    return [];
  }
}

export function saveTextSnippets(clientId: string, snippets: SavedTextSnippet[]): void {
  localStorage.setItem(textSnippetsKey(clientId), JSON.stringify(snippets.slice(0, maxSavedSnippets)));
}

export function normalizeSnippetName(value: string): string {
  return value.trim().slice(0, maxSnippetNameLength);
}
