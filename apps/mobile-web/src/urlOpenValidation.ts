const maxUrlLength = 2_048;
const controlCharacterPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const schemePattern = /^[A-Za-z][A-Za-z0-9+.-]*:/u;

export type UrlDraftValidation =
  | { valid: true; normalizedUrl: string }
  | { valid: false; message: string };

function hasExplicitScheme(value: string): boolean {
  const match = schemePattern.exec(value);
  if (!match) {
    return false;
  }

  const colonIndex = match[0].length - 1;
  const hostPart = value.slice(0, colonIndex);
  const remainder = value.slice(colonIndex + 1);
  const portMatch = /^\d+(?:[/?#]|$)/u.exec(remainder);
  const canBeBareHost = hostPart.toLowerCase() === "localhost" || hostPart.includes(".");
  return !(canBeBareHost && portMatch);
}

export function validateUrlDraft(value: string): UrlDraftValidation {
  const trimmed = value.trim();
  if (!trimmed) {
    return { valid: false, message: "Enter a web address." };
  }

  if (trimmed.length > maxUrlLength) {
    return { valid: false, message: "The web address is too long." };
  }

  if (controlCharacterPattern.test(trimmed)) {
    return { valid: false, message: "Enter a valid web address." };
  }

  const candidate = hasExplicitScheme(trimmed) ? trimmed : `https://${trimmed}`;

  let parsed: URL;
  try {
    parsed = new URL(candidate);
  } catch {
    return { valid: false, message: "Enter a valid web address." };
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return { valid: false, message: "Use an HTTP or HTTPS web address." };
  }

  if (!parsed.hostname) {
    return { valid: false, message: "Enter a valid web address." };
  }

  return { valid: true, normalizedUrl: parsed.href };
}
