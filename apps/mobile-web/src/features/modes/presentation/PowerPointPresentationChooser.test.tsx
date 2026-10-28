import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import type { ComponentProps } from "react";
import { PowerPointPresentationChooser } from "./PowerPointPresentationChooser";
function setup(overrides: Partial<ComponentProps<typeof PowerPointPresentationChooser>>) {
  const onLaunchSaved = vi.fn();
  render(
    <PowerPointPresentationChooser
      capability={{
        state: "unavailable",
        presentations: [],
        availablePresentations: [
          { presentationId: "saved", title: "Saved deck", fileName: "deck.pptx" },
        ],
      }}
      launchPending={false}
      refreshPending={false}
      appLaunchPending={false}
      onBack={vi.fn()}
      onSelectOpen={vi.fn()}
      onSelectionChange={vi.fn()}
      selection={{ kind: "saved", id: "saved" }}
      onLaunchSaved={onLaunchSaved}
      {...overrides}
    />,
  );
  return onLaunchSaved;
}
it("does not launch a saved deck while PowerPoint itself is still starting", () => {
  const launch = setup({ appLaunchPending: true });
  fireEvent.click(screen.getByRole("button", { name: "Open and present" }));
  expect(launch).not.toHaveBeenCalled();
});
it("disables a saved deck commit when its connection becomes unavailable", () => {
  const launch = setup({ enabled: false });
  fireEvent.click(screen.getByRole("button", { name: "Open and present" }));
  expect(launch).not.toHaveBeenCalled();
});
