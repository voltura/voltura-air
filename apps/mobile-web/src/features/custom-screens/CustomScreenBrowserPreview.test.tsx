import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  CustomScreenBrowserPreview,
  readCustomScreenPreviewControlDepth,
  readCustomScreenPreviewId
} from "./CustomScreenBrowserPreview";

describe("CustomScreenBrowserPreview", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("accepts only bounded opaque custom-screen IDs", () => {
    expect(readCustomScreenPreviewId(
      "http://127.0.0.1/?customScreenPreview=screen.preview-1"))
      .toBe("screen.preview-1");
    expect(readCustomScreenPreviewId(
      "http://127.0.0.1/?customScreenPreview=../../private"))
      .toBeNull();
  });

  it("reads the selected device control-depth preference", () => {
    expect(readCustomScreenPreviewControlDepth(
      "http://127.0.0.1/?customScreenPreview=screen.preview-1&controlDepth=true"))
      .toBe(true);
    expect(readCustomScreenPreviewControlDepth(
      "http://127.0.0.1/?customScreenPreview=screen.preview-1&controlDepth=false"))
      .toBe(false);
  });

  it("loads the sanitized preview definition from the loopback API", async () => {
    vi.stubGlobal("localStorage", {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn()
    });
    vi.stubGlobal("matchMedia", vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn()
    })));
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      type: "custom.screen.get.result",
      operationId: "preview",
      succeeded: true,
      screen: {
        id: "screen.preview",
        name: "Preview screen",
        revision: "revision.preview",
        orientationLayoutsEnabled: false,
        showNavigationHeader: false,
        sections: []
      }
    }), { status: 200 }));
    vi.stubGlobal("fetch", fetch);

    const view = render(
      <CustomScreenBrowserPreview
        controlDepth
        screenId="screen.preview"
      />
    );

    await waitFor(() => {
      expect(view.container.querySelector(".custom-screen-workspace"))
        .not.toBeNull();
      expect(view.container.querySelector(".custom-screen-workspace")
        ?.getAttribute("aria-label")).toBe("Preview screen");
    });
    expect(fetch).toHaveBeenCalledWith(
      "/api/custom-screens/preview/screen.preview",
      expect.objectContaining({ cache: "no-store" }));
    expect(screen.getByText("Preview only · actions are disabled")).toBeTruthy();
    const previewRoot = view.container.querySelector(
      ".custom-screen-browser-preview");
    expect(previewRoot?.classList.contains("control-depth")).toBe(true);
    expect(previewRoot?.firstElementChild?.classList.contains(
      "custom-screen-workspace")).toBe(true);
  });
});
