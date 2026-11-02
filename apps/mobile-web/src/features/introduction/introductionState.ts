import { readLocalStorage, writeLocalStorage } from "../../foundation/platform/browserStorage";
import { pcProfilesKey } from "../../foundation/connection/pcProfiles";

export const introductionKey = "voltura-air.introduction";
type IntroductionState = "eligible" | "suppressed";

// Called before the connection hook can write a newly paired PC. Unknown records
// fail closed; this record belongs to the browser, never to an individual PC.
export function initializeIntroduction(): IntroductionState {
  const raw = readLocalStorage(introductionKey);
  if (raw !== null) {
    try {
      const value: unknown = JSON.parse(raw);
      if (
        typeof value === "object" &&
        value !== null &&
        "version" in value &&
        value.version === 1 &&
        "state" in value &&
        value.state === "eligible" &&
        Object.keys(value).length === 2
      ) {
        return "eligible";
      }
    } catch {
      /* Invalid records suppress automatic presentation. */
    }
    return "suppressed";
  }
  const profiles = readLocalStorage(pcProfilesKey);
  // Even an unreadable old saved-PC record must not introduce an existing client.
  const state = profiles !== null && profiles !== "[]" ? "suppressed" : "eligible";
  saveIntroduction(state);
  return state;
}

export function saveIntroduction(state: IntroductionState): void {
  writeLocalStorage(introductionKey, JSON.stringify({ version: 1, state }));
}
