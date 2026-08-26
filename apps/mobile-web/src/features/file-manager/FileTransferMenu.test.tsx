import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { FileTransferMenu } from "./FileTransferMenu";

describe("FileTransferMenu", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("shows both one-file directions when device staging is supported and one PC file is selected", () => {
    class SupportedFileHandle {
      createWritable() {
        return Promise.resolve({});
      }
    }
    vi.stubGlobal("FileSystemFileHandle", SupportedFileHandle);
    vi.stubGlobal("navigator", { storage: { getDirectory: vi.fn() } });
    render(
      <FileTransferMenu
        activePc={{
          customName: false,
          id: "pc",
          name: "PC",
          url: "https://pc.invalid",
          hostIdentityPublicKey: "A".repeat(87),
          transportMode: "secure-direct",
        }}
        canModify
        clientId="client"
        enabled
        send={vi.fn()}
        target={{
          sessionId: "session",
          panel: "left",
          revision: "revision",
          entry: {
            id: "file",
            name: "report.pdf",
            kind: "file",
            extension: "pdf",
            size: 12,
            modifiedUtc: "2026-08-25T00:00:00Z",
            attributes: [],
          },
        }}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Transfer" }));

    expect(
      (screen.getByRole("menuitem", { name: "Save to this device" }) as HTMLButtonElement).disabled,
    ).toBe(false);
    expect(
      (screen.getByRole("menuitem", { name: "Choose file from this device" }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
  });
});
