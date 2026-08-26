(function () {
  const splitTags = (value) =>
    String(value || "")
      .split(/[\s,]+/u)
      .map((tag) => tag.trim())
      .filter(Boolean);

  const initialize = (editor) => {
    const input = editor.querySelector("[data-tag-input]");
    const value = editor.querySelector("[data-tags-value]");
    const pills = editor.querySelector("[data-tag-pills]");
    if (!input || !value || !pills) return;

    let tags = [];
    const addTags = (candidate) => {
      splitTags(candidate).forEach((tag) => {
        if (!tags.some((existing) => existing.toLocaleLowerCase() === tag.toLocaleLowerCase())) {
          tags.push(tag);
        }
      });
    };

    const render = () => {
      value.value = tags.join(", ");
      pills.replaceChildren();
      tags.forEach((tag) => {
        const pill = document.createElement("span");
        pill.className = "catalog-tag-pill";
        const label = document.createElement("span");
        label.textContent = tag;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "catalog-tag-remove";
        remove.setAttribute("aria-label", `Remove tag ${tag}`);
        remove.addEventListener("click", () => {
          tags = tags.filter((existing) => existing !== tag);
          render();
          input.focus();
        });
        pill.append(label, remove);
        pills.append(pill);
      });
    };

    const commit = () => {
      addTags(input.value);
      input.value = "";
      render();
    };

    addTags(value.value);
    render();
    input.addEventListener("keydown", (event) => {
      if (event.key === " " || event.key === ",") {
        event.preventDefault();
        commit();
      }
    });
    input.addEventListener("blur", commit);
    input.form?.addEventListener("submit", commit);
    editor.addEventListener("click", (event) => {
      if (event.target === editor || event.target === pills) {
        input.focus();
      }
    });
  };

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-tag-editor]").forEach(initialize);
  });
})();
