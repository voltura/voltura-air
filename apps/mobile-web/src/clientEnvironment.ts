const screenshotModeKey = "voltura-air.screenshotMode";

export function isScreenshotMode(source: string): boolean {
  try {
    const url = new URL(source);
    const value = url.searchParams.get("screenshot") ?? url.searchParams.get("screenshotMode");
    if (value) {
      return ["1", "true", "yes"].includes(value.toLowerCase());
    }
  } catch {
  }

  return localStorage.getItem(screenshotModeKey) === "true";
}

export function getDefaultDeviceName(): string {
  if (navigator.userAgent.includes("iPad")) {
    return "iPad";
  }

  if (navigator.userAgent.includes("iPhone")) {
    return "iPhone";
  }

  if (/Android/i.test(navigator.userAgent)) {
    return /Tablet|SM-T|Nexus 7|Nexus 10/i.test(navigator.userAgent) ? "Android tablet" : "Android phone";
  }

  return "Mobile device";
}

export function getDisplayMode(): "browser" | "installed" | "unknown" {
  if (window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true) {
    return "installed";
  }

  if (window.matchMedia("(display-mode: browser)").matches) {
    return "browser";
  }

  return "unknown";
}

export function getPlatformName(): string {
  const userAgent = navigator.userAgent;
  if (/iPad/i.test(userAgent)) {
    return "iPadOS";
  }

  if (/iPhone/i.test(userAgent)) {
    return "iOS";
  }

  if (/Android/i.test(userAgent)) {
    return "Android";
  }

  if (/Windows/i.test(userAgent)) {
    return "Windows";
  }

  if (/Mac OS X/i.test(userAgent)) {
    return "macOS";
  }

  return "Unknown platform";
}

export function getBrowserName(): string {
  const userAgent = navigator.userAgent;
  if (/SamsungBrowser/i.test(userAgent)) {
    return "Samsung Internet";
  }

  if (/Edg\//i.test(userAgent)) {
    return "Edge";
  }

  if (/CriOS|Chrome/i.test(userAgent) && !/Edg\//i.test(userAgent)) {
    return "Chrome";
  }

  if (/FxiOS|Firefox/i.test(userAgent)) {
    return "Firefox";
  }

  if (/Safari/i.test(userAgent)) {
    return "Safari";
  }

  return "Unknown browser";
}
