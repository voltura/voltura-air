export const hostAuthenticationTimeoutMs = 10_000;

export function isHostAuthenticationExpired(
  authenticationExpiresAt: number,
  now: number = Date.now(),
): boolean {
  return !Number.isFinite(authenticationExpiresAt) || authenticationExpiresAt <= now;
}

export function nextHostAuthenticationDeadline(
  authenticationDeadlines: readonly number[],
  now: number = Date.now(),
): number | null {
  const pending = authenticationDeadlines.filter(
    (deadline) => !isHostAuthenticationExpired(deadline, now),
  );
  return pending.length === 0 ? null : Math.min(...pending);
}

export function canClaimHostCandidate(
  isCurrentCandidate: boolean,
  isOpen: boolean,
  authenticationExpiresAt: number,
  now: number = Date.now(),
): boolean {
  return isCurrentCandidate && isOpen && !isHostAuthenticationExpired(authenticationExpiresAt, now);
}
