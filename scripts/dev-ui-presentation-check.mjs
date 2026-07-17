export async function verifyResponsivePresentationLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.getByRole("button", { name: "Presentation", exact: true }).click();

  const nextButton = page.getByRole("button", { name: "Next", exact: true });
  await nextButton.evaluate((button) => {
    globalThis.__volturaPresentationPendingObserved = button.hasAttribute("disabled");
    new MutationObserver((records) => {
      if (records.some((record) => record.attributeName === "disabled" && record.oldValue === null)) {
        globalThis.__volturaPresentationPendingObserved = true;
      }
    }).observe(button, { attributes: true, attributeFilter: ["disabled"], attributeOldValue: true });
  });
  await nextButton.click();
  await page.waitForFunction(() => {
    const button = document.querySelector(".presentation-navigation button:last-child");
    const error = document.querySelector(".presentation-result.error");
    return error instanceof HTMLElement ||
      (globalThis.__volturaPresentationPendingObserved === true && button instanceof HTMLButtonElement && !button.disabled);
  }, undefined, { timeout: 6000 }).catch(async (error) => {
    const resultText = await page.locator(".presentation-result.error").textContent().catch(() => "missing");
    throw new Error(`Presentation command did not complete: ${resultText}`, { cause: error });
  });
  const commandError = await page.locator(".presentation-result.error").textContent().catch(() => null);
  if (commandError !== null) {
    throw new Error(`Presentation command failed through the production host: ${commandError}`);
  }

  await selectPresentationTarget(page, "Google Slides");
  if (await page.getByRole("button", { name: "Start slideshow", exact: true }).count() !== 0 ||
      await page.getByRole("button", { name: "Blackout", exact: true }).count() !== 1 ||
      await page.getByRole("button", { name: "Laser pointer", exact: true }).count() !== 1) {
    throw new Error("Google Slides exposed an unsafe or incomplete presentation command set.");
  }

  await selectPresentationTarget(page, "PDF / browser");
  if (await page.getByRole("button", { name: "Blackout", exact: true }).count() !== 0 ||
      await page.getByRole("button", { name: "Laser pointer", exact: true }).count() !== 0 ||
      await page.getByRole("button", { name: "End slideshow", exact: true }).count() !== 1) {
    throw new Error("PDF/browser exposed a target-incompatible presentation command.");
  }

  const viewports = [
    { name: "phone portrait", width: 393, height: 852, columns: 1, exit: "bottom" },
    { name: "compact phone portrait", width: 375, height: 667, columns: 1, exit: "bottom" },
    { name: "phone landscape", width: 852, height: 393, columns: 2, exit: "compact" },
    { name: "tablet portrait", width: 768, height: 1024, columns: 2, exit: "top" },
    { name: "tablet landscape", width: 1024, height: 768, columns: 2, exit: "top" }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(({ expectedExit }) => {
      const surface = document.querySelector(".presentation-mode");
      const previous = document.querySelector(".presentation-navigation button:first-child");
      const next = document.querySelector(".presentation-navigation button:last-child");
      const targetButton = document.querySelector(".presentation-target-selector-toggle");
      const timerButtons = Array.from(document.querySelectorAll(".presentation-timer-actions button"));
      const compactExit = document.querySelector(".compact-mode-button");
      const topExit = document.querySelector(".top-mode-tabs");
      const bottomExit = document.querySelector(".bottom-mode-tabs");
      if (!(surface instanceof HTMLElement) || !(previous instanceof HTMLButtonElement) || !(next instanceof HTMLButtonElement) ||
          !(targetButton instanceof HTMLButtonElement) || timerButtons.length !== 2) {
        return { error: "Presentation controls were not visible." };
      }

      const surfaceBounds = surface.getBoundingClientRect();
      const exitElement = expectedExit === "compact" ? compactExit : expectedExit === "top" ? topExit : bottomExit;
      return {
        columns: getComputedStyle(surface).gridTemplateColumns.split(" ").filter(Boolean).length,
        exitVisible: exitElement instanceof HTMLElement && getComputedStyle(exitElement).display !== "none",
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minNavigationHeight: Math.min(previous.getBoundingClientRect().height, next.getBoundingClientRect().height),
        minTargetHeight: targetButton.getBoundingClientRect().height,
        minTimerActionHeight: Math.min(...timerButtons.map((button) => button.getBoundingClientRect().height)),
        outsideViewportWidth: surfaceBounds.left < -1 || surfaceBounds.right > window.innerWidth + 1
      };
    }, { expectedExit: viewport.exit });

    if ("error" in result || result.columns !== viewport.columns || !result.exitVisible || result.horizontalOverflow ||
        result.minNavigationHeight < 62 || result.minTargetHeight < 44 || result.minTimerActionHeight < 44 || result.outsideViewportWidth) {
      throw new Error(`Responsive Presentation check failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }
}

async function selectPresentationTarget(page, target) {
  await page.locator(".presentation-target-selector-toggle").click();
  await page.getByRole("menuitemradio", { name: target, exact: true }).click();
}
