const volatileOverrides = new Map<string, string | null>();

export function readLocalStorage(key: string, storage?: Storage): string | null {
  if (!storage && volatileOverrides.has(key)) {return volatileOverrides.get(key) ?? null;}
  try {
    return (storage ?? window.localStorage).getItem(key);
  } catch {
    return null;
  }
}

export function writeLocalStorage(key: string, value: string, storage?: Storage): boolean {
  try {
    (storage ?? window.localStorage).setItem(key, value);
    if (!storage) {volatileOverrides.delete(key);}
    return true;
  } catch {
    if (!storage) {volatileOverrides.set(key, value);}
    return false;
  }
}

export function removeLocalStorage(key: string, storage?: Storage): boolean {
  try {
    (storage ?? window.localStorage).removeItem(key);
    if (!storage) {volatileOverrides.delete(key);}
    return true;
  } catch {
    if (!storage) {volatileOverrides.set(key, null);}
    return false;
  }
}
