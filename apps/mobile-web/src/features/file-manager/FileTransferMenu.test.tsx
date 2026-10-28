import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { FileTransferMenu } from "./FileTransferMenu";
import type { ComponentProps } from "react";

const transferMock = vi.hoisted(() => ({
  cancel: vi.fn(),
  discardReadyFile: vi.fn(),
  presentation: {
    active: false,
    fileName: "",
    message: "",
    needsReplacementName: false,
    progress: 0,
    readyToSave: false,
  },
  retryUploadName: vi.fn(),
  saveReadyFile: vi.fn(),
  startDownload: vi.fn(),
  startScreenCapture: vi.fn(),
  startUpload: vi.fn(),
}));

vi.mock("../../foundation/file-transfer/useFileTransfer", () => ({
  useFileTransfer: () => transferMock,
}));

const target = {
  sessionId: "session",
  panel: "left" as const,
  revision: "revision",
  entry: {
    id: "file",
    name: "report.pdf",
    kind: "file" as const,
    extension: "pdf",
    size: 12,
    modifiedUtc: "2026-08-25T00:00:00Z",
    attributes: [],
  },
};

function renderMenu(
  canModify = true,
  overrides: Partial<ComponentProps<typeof FileTransferMenu>> = {},
) {
  return render(
    <FileTransferMenu
      activePc={{
        customName: false,
        id: "pc",
        name: "PC",
        url: "https://pc.invalid",
        hostIdentityPublicKey: "A".repeat(87),
        transportMode: "secure-direct",
      }}
      canModify={canModify}
      clientId="client"
      enabled
      send={vi.fn()}
      target={target}
      {...overrides}
    />,
  );
}

describe("FileTransferMenu", () => {
  it("makes transfer unavailable when the connection is disabled", () => {
    renderMenu(true, { enabled: false });
    const trigger = screen.getByRole<HTMLButtonElement>("button", { name: "Transfer" });
    expect(trigger.disabled).toBe(true);
  });
  it.each(["permission", "destination", "connection"])(
    "rejects a pending picker selection after %s changes",
    (reason) => {
      const notice = vi.fn();
      const host = {
        customName: false,
        id: "pc",
        name: "PC",
        url: "https://pc.invalid",
        hostIdentityPublicKey: "A".repeat(87),
        transportMode: "secure-direct" as const,
      };
      const shared = {
        activePc: host,
        canModify: true,
        clientId: "client",
        enabled: true,
        send: vi.fn(),
        target,
        onTransferNotice: notice,
      };
      const view = render(<FileTransferMenu {...shared} />);
      fireEvent.click(screen.getByRole("button", { name: "Transfer" }));
      fireEvent.click(screen.getByRole("menuitem", { name: "Choose file from this device" }));
      view.rerender(
        <FileTransferMenu
          {...shared}
          canModify={reason !== "permission"}
          enabled={reason !== "connection"}
          target={reason === "destination" ? { ...target, revision: "next-folder" } : target}
        />,
      );
      fireEvent.change(document.querySelector('input[type="file"]')!, {
        target: { files: [new File(["data"], "file.txt")] },
      });
      expect(transferMock.startUpload).not.toHaveBeenCalled();
      expect(notice).toHaveBeenCalledWith(expect.stringContaining("changed"), "error");
    },
  );
  it("dismisses the transfer menu with Escape and returns trigger focus", () => {
    renderMenu();
    const trigger = screen.getByRole("button", { name: "Transfer" });
    fireEvent.click(trigger);
    screen.getByRole("menuitem", { name: "Choose file from this device" }).focus();
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    vi.unstubAllGlobals();
    Object.assign(transferMock.presentation, {
      active: false,
      fileName: "",
      message: "",
      needsReplacementName: false,
      progress: 0,
      readyToSave: false,
    });
  });

  it("shows both one-file directions and photo capture", () => {
    class SupportedFileHandle {
      createWritable() {
        return Promise.resolve({});
      }
    }
    vi.stubGlobal("FileSystemFileHandle", SupportedFileHandle);
    vi.stubGlobal("navigator", { storage: { getDirectory: vi.fn() } });
    renderMenu();

    fireEvent.click(screen.getByRole("button", { name: "Transfer" }));

    expect(
      (screen.getByRole("menuitem", { name: "Save to this device" }) as HTMLButtonElement).disabled,
    ).toBe(false);
    expect(
      (screen.getByRole("menuitem", { name: "Choose file from this device" }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
    expect(
      (screen.getByRole("menuitem", { name: "Take photo" }) as HTMLButtonElement).disabled,
    ).toBe(false);
  });

  it("uploads each captured photo through the existing one-file flow", () => {
    renderMenu();

    fireEvent.click(screen.getByRole("button", { name: "Transfer" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Take photo" }));

    const input = document.querySelector<HTMLInputElement>('input[type="file"][accept="image/*"]');
    expect(input?.getAttribute("capture")).toBe("environment");
    expect(transferMock.startUpload).not.toHaveBeenCalled();

    const photo = new File(["photo"], "IMG_0001.jpg", { type: "image/jpeg" });
    fireEvent.change(input!, { target: { files: [photo] } });

    expect(input?.value).toBe("");
    expect(transferMock.startUpload).toHaveBeenCalledExactlyOnceWith(target, photo);

    fireEvent.change(input!, { target: { files: [photo] } });
    expect(transferMock.startUpload).toHaveBeenCalledTimes(2);
  });

  it("disables photo capture when file changes are blocked or a transfer is busy", () => {
    const blocked = renderMenu(false);
    fireEvent.click(screen.getByRole("button", { name: "Transfer" }));
    expect(
      (screen.getByRole("menuitem", { name: "Take photo" }) as HTMLButtonElement).disabled,
    ).toBe(true);

    blocked.unmount();
    transferMock.presentation.active = true;
    renderMenu();
    fireEvent.click(screen.getByRole("button", { name: "Transfer" }));
    expect(
      (screen.getByRole("menuitem", { name: "Take photo" }) as HTMLButtonElement).disabled,
    ).toBe(true);
  });
});
