export function createLocalId(): string {
  const values = crypto.getRandomValues(new Uint32Array(4));
  return Array.from(values, (value) => value.toString(36).padStart(7, "0")).join("-");
}
