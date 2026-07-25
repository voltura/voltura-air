export async function verifyResponsivePresentationLayout(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  await page.getByLabel("Tools").getByRole("button", { name: "Presentation", exact: true }).click();
  await selectPresentationTarget(page, "Google Slides");

  if (await page.getByRole("button", { name: "Start slideshow", exact: true }).count() !== 0 ||
      await page.getByRole("button", { name: "Blackout", exact: true }).count() !== 1 ||
      await page.getByRole("button", { name: "Laser pointer", exact: true }).count() !== 1) {
    throw new Error("Google Slides exposed an unsafe or incomplete presentation command set.");
  }

  await selectPresentationTarget(page, "PDF / browser");
  if (await page.getByRole("button", { name: "Blackout", exact: true }).count() !== 0 ||
      await page.getByRole("button", { name: "Laser pointer", exact: true }).count() !== 1 ||
      await page.getByRole("button", { name: "End slideshow", exact: true }).count() !== 1) {
    throw new Error("PDF/browser exposed a target-incompatible presentation command.");
  }

  const viewports = [
    { name: "phone portrait", width: 393, height: 852, columns: 1, exit: "bottom", minNavigationHeight: 58 },
    { name: "compact phone portrait", width: 375, height: 667, columns: 1, exit: "bottom", minNavigationHeight: 58 },
    { name: "phone landscape", width: 852, height: 393, columns: 2, exit: "compact", minNavigationHeight: 62 },
    { name: "tablet portrait", width: 768, height: 1024, columns: 2, exit: "top", minNavigationHeight: 62 },
    { name: "tablet landscape", width: 1024, height: 768, columns: 2, exit: "top", minNavigationHeight: 62 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const result = await page.evaluate(({ expectedExit }) => {
      const surface = document.querySelector(".presentation-mode");
      const heading = document.querySelector(".presentation-header h1");
      const primaryControls = document.querySelector(".presentation-control-primary");
      const secondaryControls = document.querySelector(".presentation-control-secondary");
      const firstSecondaryAction = document.querySelector(".presentation-control-secondary button");
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
      const headingBounds = heading?.getBoundingClientRect();
      const primaryBounds = primaryControls?.getBoundingClientRect();
      const secondaryBounds = secondaryControls?.getBoundingClientRect();
      const firstSecondaryActionBounds = firstSecondaryAction?.getBoundingClientRect();
      const targetBounds = targetButton.getBoundingClientRect();
      const shortLandscape = window.matchMedia("(height <= 520px) and (orientation: landscape)").matches;
      const targetVisible = targetBounds.width > 0 && targetBounds.height > 0;
      const exitElement = expectedExit === "compact" ? compactExit : expectedExit === "top" ? topExit : bottomExit;
      return {
        columns: getComputedStyle(surface).gridTemplateColumns.split(" ").filter(Boolean).length,
        exitVisible: exitElement instanceof HTMLElement && getComputedStyle(exitElement).display !== "none",
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        minNavigationHeight: Math.min(previous.getBoundingClientRect().height, next.getBoundingClientRect().height),
        minTargetHeight: targetBounds.height,
        targetRequiresHeight: !shortLandscape,
        minTimerActionHeight: Math.min(...timerButtons.map((button) => button.getBoundingClientRect().height)),
        outsideViewportWidth: surfaceBounds.left < -1 || surfaceBounds.right > window.innerWidth + 1,
        shortLandscapeHeadingVisible: shortLandscape && (headingBounds?.width ?? 0) > 1,
        shortLandscapeLeftInset: shortLandscape && primaryBounds
          ? Math.abs(primaryBounds.top - surfaceBounds.top)
          : 0,
        shortLandscapeColumnMisalignment:
          shortLandscape && primaryBounds && secondaryBounds
            ? Math.abs(primaryBounds.top - secondaryBounds.top)
            : 0,
        shortLandscapeTargetVisible: shortLandscape && targetVisible,
        targetHiddenOutsideShortLandscape: !shortLandscape && !targetVisible,
        firstSecondaryActionVisible: firstSecondaryActionBounds
          ? firstSecondaryActionBounds.height > 0
          : false
      };
    }, { expectedExit: viewport.exit });

    if ("error" in result || result.columns !== viewport.columns || !result.exitVisible || result.horizontalOverflow ||
        result.minNavigationHeight < viewport.minNavigationHeight ||
        (result.targetRequiresHeight && result.minTargetHeight < 44) ||
        result.minTimerActionHeight < 44 || result.outsideViewportWidth ||
        result.shortLandscapeHeadingVisible || result.shortLandscapeLeftInset > 1 ||
        result.shortLandscapeColumnMisalignment > 1 ||
        result.shortLandscapeTargetVisible || result.targetHiddenOutsideShortLandscape ||
        !result.firstSecondaryActionVisible) {
      throw new Error(`Responsive Presentation check failed for ${viewport.name}: ${JSON.stringify(result)}`);
    }
  }

  await page.setViewportSize({ width: 852, height: 393 });
  await page.locator(".presentation-trackpad-heading").click();
  const expandedLayout = await page.evaluate(() => {
    const surface = document.querySelector(".presentation-mode");
    const controls = document.querySelector(".presentation-controls-panel");
    const sideStack = document.querySelector(".presentation-side-stack");
    const summaryDetails = document.querySelector(".presentation-trackpad-summary > div");
    const summaryPrevious = document.querySelector(".presentation-trackpad-summary button:first-of-type");
    const summaryNext = document.querySelector(".presentation-trackpad-summary button:last-of-type");
    if (!(surface instanceof HTMLElement) || !(controls instanceof HTMLElement) ||
        !(sideStack instanceof HTMLElement) || !(summaryDetails instanceof HTMLElement) ||
        !(summaryPrevious instanceof HTMLButtonElement) || !(summaryNext instanceof HTMLButtonElement)) {
      return { error: "Expanded presentation trackpad controls were not visible." };
    }

    const surfaceBounds = surface.getBoundingClientRect();
    const controlsBounds = controls.getBoundingClientRect();
    const sideBounds = sideStack.getBoundingClientRect();
    const detailsBounds = summaryDetails.getBoundingClientRect();
    const previousBounds = summaryPrevious.getBoundingClientRect();
    const nextBounds = summaryNext.getBoundingClientRect();
    return {
      halfWidthDifference: Math.abs(controlsBounds.width - sideBounds.width),
      leftOutsideSurface: Math.abs(controlsBounds.left - surfaceBounds.left),
      rightOutsideSurface: Math.abs(sideBounds.right - surfaceBounds.right),
      summaryOverlapsNavigation:
        detailsBounds.bottom > previousBounds.top + 1 ||
        detailsBounds.bottom > nextBounds.top + 1,
      navigationRowsMisaligned: Math.abs(previousBounds.top - nextBounds.top),
      navigationWidthsDiffer: Math.abs(previousBounds.width - nextBounds.width),
      minNavigationHeight: Math.min(previousBounds.height, nextBounds.height),
      navigationLabelsVisible:
        summaryPrevious.textContent?.includes("Previous") === true &&
        summaryNext.textContent?.includes("Next") === true
    };
  });

  if ("error" in expandedLayout || expandedLayout.halfWidthDifference > 1 ||
      expandedLayout.leftOutsideSurface > 1 || expandedLayout.rightOutsideSurface > 1 ||
      expandedLayout.summaryOverlapsNavigation || expandedLayout.navigationRowsMisaligned > 1 ||
      expandedLayout.navigationWidthsDiffer > 1 || expandedLayout.minNavigationHeight < 62 ||
      !expandedLayout.navigationLabelsVisible) {
    throw new Error(`Expanded Presentation trackpad layout failed: ${JSON.stringify(expandedLayout)}`);
  }

  await page.getByRole("button", { name: "Expand trackpad", exact: true }).click();
  await page.getByRole("button", { name: "Restore trackpad", exact: true }).click();
  if (await page.locator(".presentation-mode.trackpad-open").count() !== 0 ||
      await page.locator(".presentation-trackpad-heading").getAttribute("aria-expanded") !== "false") {
    throw new Error("Restoring the fullscreen Presentation trackpad did not fold its panel.");
  }

  await page.setViewportSize({ width: 393, height: 852 });
  await page.locator(".presentation-trackpad-heading").click();
  const portraitSummary = await page.evaluate(() => {
    const details = document.querySelector(".presentation-trackpad-summary-details");
    const previous = document.querySelector(".presentation-trackpad-summary-navigation button:first-child");
    const next = document.querySelector(".presentation-trackpad-summary-navigation button:last-child");
    if (!(details instanceof HTMLElement) || !(previous instanceof HTMLButtonElement) ||
        !(next instanceof HTMLButtonElement)) {
      return { error: "Portrait expanded-trackpad summary was not visible." };
    }

    const detailsBounds = details.getBoundingClientRect();
    const previousBounds = previous.getBoundingClientRect();
    const nextBounds = next.getBoundingClientRect();
    return {
      summaryOverlapsNavigation:
        detailsBounds.bottom > previousBounds.top + 1 ||
        detailsBounds.bottom > nextBounds.top + 1,
      navigationRowsMisaligned: Math.abs(previousBounds.top - nextBounds.top),
      navigationWidthsDiffer: Math.abs(previousBounds.width - nextBounds.width),
      navigationHeight: Math.min(previousBounds.height, nextBounds.height),
      labelsVisible:
        previous.textContent?.includes("Previous") === true &&
        next.textContent?.includes("Next") === true
    };
  });
  if ("error" in portraitSummary || portraitSummary.summaryOverlapsNavigation ||
      portraitSummary.navigationRowsMisaligned > 1 ||
      portraitSummary.navigationWidthsDiffer > 1 ||
      Math.abs(portraitSummary.navigationHeight - expandedLayout.minNavigationHeight) > 1 ||
      !portraitSummary.labelsVisible) {
    throw new Error(`Portrait Presentation trackpad summary failed: ${JSON.stringify(portraitSummary)}`);
  }
  await page.locator(".presentation-trackpad-heading").click();

  await selectPresentationTarget(page, "PowerPoint");
  await page.getByRole("button", { name: "Open menu", exact: true }).click();
  const settingsCloseAppearance = await page.getByRole("button", { name: "Close menu", exact: true })
    .evaluate(readCloseControlAppearance);
  await page.getByRole("button", { name: "Close menu", exact: true }).click();
  const goToSlide = page.getByRole("button", { name: "Go to slide", exact: true });
  if (await goToSlide.count() > 0) {
    await goToSlide.click();
  } else {
    await page.getByRole("button", { name: "About Presentation guidance", exact: true }).click();
  }
  const closeAlignment = await page.evaluate(() => {
    const closeButton = document.querySelector(".modal-dialog-close");
    const closeIcon = closeButton?.querySelector("svg");
    if (!(closeButton instanceof HTMLButtonElement) || !(closeIcon instanceof SVGElement)) {
      return { error: "Shared modal close control was not visible." };
    }

    const buttonBounds = closeButton.getBoundingClientRect();
    const iconBounds = closeIcon.getBoundingClientRect();
    return {
      horizontalOffset: Math.abs(
        buttonBounds.left + buttonBounds.width / 2 -
        (iconBounds.left + iconBounds.width / 2)),
      verticalOffset: Math.abs(
        buttonBounds.top + buttonBounds.height / 2 -
        (iconBounds.top + iconBounds.height / 2)),
      appearance: {
        backgroundColor: getComputedStyle(closeButton).backgroundColor,
        borderBottomColor: getComputedStyle(closeButton).borderBottomColor,
        borderLeftColor: getComputedStyle(closeButton).borderLeftColor,
        borderRightColor: getComputedStyle(closeButton).borderRightColor,
        borderTopColor: getComputedStyle(closeButton).borderTopColor,
        color: getComputedStyle(closeButton).color
      }
    };
  });
  if ("error" in closeAlignment || closeAlignment.horizontalOffset > 1 ||
      closeAlignment.verticalOffset > 1 ||
      JSON.stringify(closeAlignment.appearance) !== JSON.stringify(settingsCloseAppearance)) {
    throw new Error(`Shared modal close control alignment failed: ${JSON.stringify(closeAlignment)}`);
  }
  await page.locator(".modal-dialog-close").click();
}

function readCloseControlAppearance(button) {
  const style = getComputedStyle(button);
  return {
    backgroundColor: style.backgroundColor,
    borderBottomColor: style.borderBottomColor,
    borderLeftColor: style.borderLeftColor,
    borderRightColor: style.borderRightColor,
    borderTopColor: style.borderTopColor,
    color: style.color
  };
}

async function selectPresentationTarget(page, target) {
  await page.locator(".presentation-target-selector-toggle").click();
  await page.getByRole("menuitemradio", { name: target, exact: true }).click();
}
