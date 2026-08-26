import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { parseThirdPartyNotices, ThirdPartyNoticesWorkspace } from "./ThirdPartyNoticesWorkspace";

const noticeSource = `VOLTURA AIR MOBILE WEB THIRD-PARTY SOFTWARE NOTICES
====================================================

------------------------------------------------------------------------
alpha 1.0.0
License: MIT
Source: https://example.com/alpha
------------------------------------------------------------------------

Alpha license text.

------------------------------------------------------------------------
beta 2.0.0
License: Apache-2.0
Source: https://example.com/beta
------------------------------------------------------------------------

Beta license text.
`;

describe("ThirdPartyNoticesWorkspace", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(noticeSource),
      }),
    );
  });

  it("parses the generated notice sections into app content", () => {
    expect(parseThirdPartyNotices(noticeSource)).toEqual([
      {
        name: "alpha 1.0.0",
        license: "MIT",
        source: "https://example.com/alpha",
        text: "Alpha license text.",
      },
      {
        name: "beta 2.0.0",
        license: "Apache-2.0",
        source: "https://example.com/beta",
        text: "Beta license text.",
      },
    ]);
  });

  it("loads notices as a navigable workspace", async () => {
    const onBack = vi.fn();
    render(<ThirdPartyNoticesWorkspace onBack={onBack} />);

    expect(screen.getByRole("status").textContent).toContain("Loading notices");
    await waitFor(() => expect(screen.getByRole("heading", { name: "alpha 1.0.0" })).toBeTruthy());
    expect(screen.getByText("Alpha license text.")).toBeTruthy();
    expect(screen.getAllByRole("link", { name: /Source/ })[0]?.getAttribute("href")).toBe(
      "https://example.com/alpha",
    );

    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(onBack).toHaveBeenCalledOnce();
  });
});
