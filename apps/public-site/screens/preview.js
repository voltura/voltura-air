document.addEventListener("change", (event) => {
  const select = event.target.closest?.(".screen-preview-device");
  if (!select) return;
  const preview = select.closest(".screen-preview");
  updatePreviewSize(preview);
});

for (const query of document.querySelectorAll("[data-catalog-query]")) {
  const input = query.querySelector("input");
  const clear = query.querySelector(".catalog-query-clear");
  if (!input || !clear) continue;
  const update = () => {
    clear.hidden = input.value.length === 0;
  };
  update();
  input.addEventListener("input", update);
  clear.addEventListener("click", () => {
    input.value = "";
    update();
    input.focus();
  });
}

for (const sort of document.querySelectorAll("[data-catalog-sort]")) {
  const select = sort.querySelector("select");
  const trigger = sort.querySelector(".catalog-sort-trigger");
  const options = sort.querySelector(".catalog-sort-options");
  if (!select || !trigger || !options) continue;
  sort.classList.add("is-enhanced");

  const close = (restoreFocus = false) => {
    trigger.setAttribute("aria-expanded", "false");
    options.hidden = true;
    if (restoreFocus) trigger.focus();
  };

  const open = () => {
    options.hidden = false;
    trigger.setAttribute("aria-expanded", "true");
    options.querySelector(`[data-sort-value="${CSS.escape(select.value)}"]`)?.focus();
  };

  const choose = (option) => {
    select.value = option.dataset.sortValue;
    trigger.querySelector("span").textContent = option.textContent;
    for (const item of options.querySelectorAll("[role=option]")) {
      item.setAttribute("aria-selected", item === option ? "true" : "false");
    }
    close(true);
  };

  trigger.addEventListener("click", () => {
    if (options.hidden) open();
    else close(true);
  });
  trigger.addEventListener("keydown", (event) => {
    if (event.key === "ArrowDown" || event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      open();
    }
  });
  options.addEventListener("click", (event) => {
    const option = event.target.closest?.("[role=option]");
    if (option) choose(option);
  });
  options.addEventListener("keydown", (event) => {
    const option = event.target.closest?.("[role=option]");
    if (!option) return;
    const items = [...options.querySelectorAll("[role=option]")];
    const index = items.indexOf(option);
    if (event.key === "Escape") {
      event.preventDefault();
      close(true);
    } else if (event.key === "ArrowDown" && items[index + 1]) {
      event.preventDefault();
      items[index + 1].focus();
    } else if (event.key === "ArrowUp" && items[index - 1]) {
      event.preventDefault();
      items[index - 1].focus();
    } else if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      choose(option);
    }
  });
  document.addEventListener("click", (event) => {
    if (!sort.contains(event.target)) close();
  });
}

document.addEventListener("click", (event) => {
  const ratingOpen = event.target.closest?.("[data-rating-dialog-open]");
  if (ratingOpen) {
    document.querySelector(".catalog-rating-dialog")?.showModal();
    return;
  }
  const deleteOpen = event.target.closest?.("[data-delete-dialog-open]");
  if (deleteOpen) {
    const deleteDialog = deleteOpen.closest("article")?.querySelector(".catalog-delete-dialog")
      ?? document.querySelector(".catalog-delete-dialog");
    deleteDialog?.showModal();
    return;
  }
  const deleteClose = event.target.closest?.("[data-delete-dialog-close]");
  if (deleteClose) {
    deleteClose.closest("dialog")?.close();
    return;
  }
  const removeRejectedOpen = event.target.closest?.("[data-remove-rejected-dialog-open]");
  if (removeRejectedOpen) {
    document.querySelector(".catalog-remove-rejected-dialog")?.showModal();
    return;
  }
  const removeRejectedClose = event.target.closest?.("[data-remove-rejected-dialog-close]");
  if (removeRejectedClose) {
    removeRejectedClose.closest("dialog")?.close();
    return;
  }
  const rotate = event.target.closest?.(".screen-preview-rotate");
  if (!rotate) return;
  const preview = rotate.closest(".screen-preview");
  preview.dataset.orientation = preview.dataset.orientation === "portrait" ? "landscape" : "portrait";
  updatePreviewSize(preview);
});

document.addEventListener("pointerover", (event) => updateRatingHero(event.target.closest?.("[data-rating-value]")?.dataset.ratingValue));
document.addEventListener("focusin", (event) => updateRatingHero(event.target.closest?.("[data-rating-value]")?.dataset.ratingValue));

document.querySelector(".star-picker")?.addEventListener("pointerleave", restoreRatingHero);

function updateRatingHero(value) {
  if (!value) return;
  const hero = document.querySelector(".catalog-rating-hero");
  if (hero) hero.querySelector("strong").textContent = value;
}

function restoreRatingHero() {
  const hero = document.querySelector(".catalog-rating-hero");
  if (hero) hero.querySelector("strong").textContent = hero.dataset.currentRating || "?";
}

document.addEventListener("click", (event) => {
  const dialog = event.target.closest?.(".catalog-rating-dialog, .catalog-delete-dialog, .catalog-remove-rejected-dialog");
  if (dialog && event.target === dialog) dialog.close();
});

function updatePreviewSize(preview) {
  const selected = preview.querySelector(".screen-preview-device")?.selectedOptions[0];
  if (!selected) return;
  const landscape = preview.dataset.orientation === "landscape";
  const portraitWidth = Number(selected.dataset.width);
  const portraitHeight = Number(selected.dataset.height);
  const width = landscape ? portraitHeight : portraitWidth;
  const height = landscape ? portraitWidth : portraitHeight;
  preview.querySelector(".screen-preview-size").textContent = `${width} × ${height}`;
  preview.querySelector(".screen-preview-viewport")?.scrollTo({ top: 0, left: 0 });
  sizeRealDevicePreview(preview, width, height);
}

function sizeRealDevicePreview(preview, width, height) {
  if (!preview.classList.contains("real-device-preview")) return;
  const stage = preview.querySelector(".screen-preview-stage");
  const frame = preview.querySelector(".screen-preview-frame");
  const iframe = frame?.querySelector("iframe");
  if (!stage || !frame || !iframe) return;
  const availableWidth = Math.max(1, stage.clientWidth - 28);
  const availableHeight = Math.max(1, stage.clientHeight - 28);
  const scale = Math.min(1, availableWidth / width, availableHeight / height);
  frame.style.width = `${Math.round(width * scale)}px`;
  frame.style.height = `${Math.round(height * scale)}px`;
  iframe.style.width = `${width}px`;
  iframe.style.height = `${height}px`;
  iframe.style.transform = `scale(${scale})`;
}

for (const preview of document.querySelectorAll(".real-device-preview")) {
  updatePreviewSize(preview);
  new ResizeObserver(() => updatePreviewSize(preview)).observe(preview.querySelector(".screen-preview-stage"));
}

const toast = document.querySelector(".catalog-toast");
if (toast) {
  const url = new URL(window.location.href);
  url.searchParams.delete("submitted");
  url.searchParams.delete("rated");
  url.searchParams.delete("ratingRemoved");
  url.searchParams.delete("deleted");
  url.searchParams.delete("reported");
  history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  setTimeout(() => {
    toast.classList.add("dismissed");
    setTimeout(() => toast.remove(), 180);
  }, 2400);
}

for (const form of document.querySelectorAll("form[data-loading-label]")) {
  form.addEventListener("submit", () => {
    const status = form.querySelector("[data-submit-status]");
    if (status) {
      status.textContent = form.dataset.loadingLabel;
      status.hidden = false;
    }
    for (const button of form.querySelectorAll("button[type='submit'], input[type='submit']")) {
      button.disabled = true;
    }
  });
}
