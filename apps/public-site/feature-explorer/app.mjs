import {
  SOURCE,
  devices,
  modes,
  goals,
  permissions,
  features,
  evaluate,
  permissionState,
  getFeatures,
  cleanState,
  setupSteps,
} from "./catalog.mjs";
import { icons } from "./icons.mjs";
const $ = (s) => document.querySelector(s);
const esc = (s) =>
  String(s).replace(
    /[&<>"']/g,
    (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c],
  );
const icon = (name, cls = "") =>
  `<svg class="${cls}" viewBox="0 0 24 24" aria-hidden="true">${icons[name] || icons["circle-help"]}</svg>`;
function hydrate(root = document) {
  root.querySelectorAll("i[data-icon]").forEach((e) => (e.outerHTML = icon(e.dataset.icon)));
}
let state = cleanState(Object.fromEntries(new URLSearchParams(location.search)));
let selected = null,
  toastTimer;
const params = new URLSearchParams(location.search);
$("#search").value = (params.get("q") || "").slice(0, 200);
$("#hide-blocked").checked = params.get("ready") === "1";
function setUrl(push = false) {
  const u = new URL(location.href);
  u.search = "";
  u.hash = "";
  for (const [k, v] of Object.entries(state)) u.searchParams.set(k, v);
  const q = $("#search").value.trim();
  if (q) u.searchParams.set("q", q);
  if ($("#hide-blocked").checked) u.searchParams.set("ready", "1");
  if (selected) u.searchParams.set("feature", selected);
  history[push ? "pushState" : "replaceState"](null, "", u);
}
function profileLabel() {
  return state.profile === "my"
    ? "My device"
    : state.profile === "remote"
      ? "Remote controls"
      : "Custom";
}
function notify(message) {
  const t = $("#toast");
  t.textContent = message;
  t.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove("show"), 4000);
}
function update(patch, focusId) {
  state = cleanState({ ...state, ...patch });
  render();
  setUrl(true);
  if (focusId) document.querySelector(focusId)?.focus();
}
function renderConfig() {
  const family = devices[state.device].family;
  $("#device-types").innerHTML = ["Phone", "Tablet", "Computer"]
    .map(
      (f, i) =>
        `<button class="device-type" data-family="${f}" aria-pressed="${f === family}">${icon(["smartphone", "tablet", "monitor"][i])}<span>${f}</span></button>`,
    )
    .join("");
  $("#device-detail").innerHTML = Object.entries(devices)
    .filter(([, d]) => d.family === family)
    .map(
      ([id, d]) =>
        `<option value="${id}" ${id === state.device ? "selected" : ""}>${d.label}${id === "computer" ? " · any OS" : " · " + d.browser}</option>`,
    )
    .join("");
  $("#connection-options").innerHTML = Object.entries(modes)
    .map(
      ([id, m]) =>
        `<button class="connection-option" data-mode="${id}" aria-pressed="${id === state.mode}">${icon(m.icon)}<span><strong>${m.label}</strong><small>${m.subtitle}</small></span><span class="radio-dot" aria-hidden="true"></span></button>`,
    )
    .join("");
  $("#goal-options").innerHTML = goals
    .map(
      (g) =>
        `<button class="goal-option" data-goal="${g.id}" aria-pressed="${g.id === state.goal}">${icon(g.icon)}<span>${g.label}</span>${g.id === state.goal ? icon("chevron-right", "goal-arrow") : ""}</button>`,
    )
    .join("");
  $("#profile").value = state.profile;
  $("#profile-note").textContent =
    state.profile === "my"
      ? "Allows all product permissions. Use for your trusted personal device."
      : state.profile === "remote"
        ? "A limited remote: screen viewing, file access, camera and Terminal are blocked."
        : "Individual Allow / Block choices live on the PC. Review the required permissions for each feature.";
}
function renderSummary() {
  const d = devices[state.device],
    m = modes[state.mode];
  $("#connection-summary").innerHTML =
    `<div class="route-copy"><div class="eyebrow">${icon("check", "tiny-icon")} YOUR SELECTED CONNECTION</div><h3>${d.label} <span aria-hidden="true">↔</span> Windows PC</h3><p>${m.description}</p></div><div class="route-visual" aria-hidden="true"><span class="route-end">${icon(d.icon)}<span>${d.family}</span></span><span class="route-path"></span><span class="route-middle">${icon(state.mode === "relay" ? "cloud" : "wifi")}</span><span class="route-path"></span><span class="route-end">${icon("monitor")}<span>Windows 11</span></span></div><div class="route-bottom">${icon(m.icon)}<span>${m.route}</span></div>`;
}
function statusMarkup(r) {
  return `<span class="feature-status ${r.status}-text">${icon(r.status === "available" ? "check" : r.status === "conditional" ? "circle-help" : "circle-minus")}${r.label}</span>`;
}
function renderResults() {
  let list = getFeatures(state, $("#search").value);
  const total = list.length;
  if ($("#hide-blocked").checked)
    list = list.filter((f) => evaluate(f, state).status !== "blocked");
  $("#result-count").textContent = list.length;
  const heading =
    state.goal === "all"
      ? "Explore your capabilities"
      : goals.find((g) => g.id === state.goal).label;
  $("#result-title").firstChild.textContent = heading + " ";
  $("#result-announcement").textContent =
    `${list.length} capabilities for ${devices[state.device].label}, ${modes[state.mode].label}, ${profileLabel()} profile.`;
  $("#feature-grid").innerHTML = list.length
    ? list
        .map((f) => {
          const r = evaluate(f, state);
          return `<article class="feature-card" data-feature-card="${f.id}"><div class="card-main"><div class="card-top"><span class="feature-icon">${icon(f.icon)}</span>${statusMarkup(r)}</div><h3>${f.title}</h3><p>${f.summary}</p><div class="card-permissions">${
            f.permissions.length
              ? f.permissions
                  .slice(0, 2)
                  .map(
                    (p) =>
                      `<span class="permission-pill">${icon("shield-check")}${permissions[p]}</span>`,
                  )
                  .join("") +
                (f.permissions.length > 2
                  ? `<span class="permission-pill">+${f.permissions.length - 2}</span>`
                  : "")
              : `<span class="permission-pill">${icon("shield-check")}${f.myOnly ? "My device only" : "Per-action permissions"}</span>`
          }</div></div><button class="card-footer" data-feature="${f.id}" aria-label="View setup for ${f.title}"><span>${r.status === "blocked" ? "See what’s needed" : "Capabilities & setup"}</span>${icon("arrow-right")}</button></article>`;
        })
        .join("")
    : `<div class="empty">${icon("search")}<h3>No matching capabilities</h3><p>${total ? "These features need a setup change. Show them to see how to enable them." : "Try a different search or explore all goals for this device."}</p><button class="primary-button" id="clear-filters">${total ? "Show unavailable features" : "Clear search & show all"}</button></div>`;
}
function render() {
  renderConfig();
  renderSummary();
  renderResults();
  if (selected && $("#feature-dialog").open) renderDetail();
}
function listHtml(items, type = "check") {
  return `<ul class="detail-list">${items.map((x) => `<li>${icon(type)}<span>${esc(x)}</span></li>`).join("")}</ul>`;
}
function permissionRows(f) {
  const entries = [...f.permissions.map((p) => [p, null]), ...Object.entries(f.optional || {})];
  return entries.length
    ? `<div class="permission-rows">${entries
        .map(([key, reason]) => {
          const status = permissionState(key, state.profile);
          return `<div class="permission-row"><span>${permissions[key]}${reason ? `<small>Optional · ${reason}</small>` : "<small>Required</small>"}</span><span class="permission-result ${status === "Allowed" ? "allowed" : status === "Blocked" ? "denied" : "unknown"}">${status}</span></div>`;
        })
        .join("")}</div>`
    : `<p class="permission-help">${f.myOnly ? "Requires the exact My device profile. There is no separate AI Assistant permission." : "Permissions belong to each control’s action. Review them on the PC; this screen grants no additional access."}</p>`;
}
function relayLimit(f) {
  return f.media && state.mode === "relay"
    ? "Relay media and data peers use a shared service allowance. At 750 GB estimated monthly TURN transfer, media is restricted to Data saver; at 850 GB, new credentials stop while ordinary command relay remains available. These are service-wide thresholds, not your personal monthly allowance. See the PC’s Connection page for current usage."
    : null;
}
function renderDetail() {
  const f = features.find((f) => f.id === selected);
  if (!f) return;
  const r = evaluate(f, state),
    steps = setupSteps(f, state);
  const notices = [...r.blockers.map((b) => b.text), ...r.checks];
  $("#feature-detail").innerHTML =
    `<div class="detail-title-row"><span class="feature-icon">${icon(f.icon)}</span><h2 id="feature-title" tabindex="-1">${f.title}</h2></div><div class="detail-context"><span>${devices[state.device].label}</span><span>${modes[state.mode].label}</span><span>${profileLabel()}</span></div><p class="lead">${f.summary}</p><div class="status-notice ${r.status}-notice"><strong>${icon(r.status === "available" ? "check" : "info")}${r.label}${r.status === "available" ? " with this setup" : ""}</strong>${notices.length ? notices.map((n) => `<p>${esc(n)}</p>`).join("") : "<p>Supported by the documented product rules with the selected access profile.</p>"}${r.blockers.length ? `<div class="notice-actions">${r.blockers.some((b) => b.code === "https") ? '<button class="primary-button" data-fix="https">Use Enhanced Direct</button>' : ""}${r.blockers.some((b) => b.code === "device") ? '<button class="primary-button" data-fix="device">Explore with iPhone</button>' : ""}${r.blockers.some((b) => ["profile", "permission"].includes(b.code)) ? '<button class="primary-button" data-fix="profile">Explore with My device</button>' : ""}</div>` : ""}</div><section class="detail-section"><h3>${icon("check-check")} What you can do</h3>${listHtml(f.capabilities)}</section><section class="detail-section"><h3>${icon("shield-check")} Permissions on the PC</h3>${permissionRows(f)}<p class="permission-help">Change access in Windows host → Devices → this device. The explorer does not change these settings.</p></section><section class="detail-section"><h3>${icon("info")} Before you use it</h3>${listHtml([...f.limits, ...(relayLimit(f) ? [relayLimit(f)] : [])], "minus")}</section><section class="setup-section"><div class="setup-heading"><h3>Your setup path</h3><button class="subtle-button" id="copy-guide">${icon("copy")} Copy guide</button></div>${r.blockers.length ? '<p class="permission-help">Resolve the requirements above to use this feature. The steps below describe the currently selected connection.</p>' : ""}<ol class="setup-steps">${steps.map((s, i) => `<li class="setup-step"><span class="setup-step-num">${i + 1}</span><div><strong>${s.title}</strong><p>${esc(s.text)}</p></div></li>`).join("")}</ol><div class="guide-actions"><a class="primary-button" href="${SOURCE.base}/releases/latest" target="_blank" rel="noopener noreferrer">Get Voltura Air for Windows ${icon("arrow-up-right")}</a><a class="secondary-button" href="${SOURCE.base}/blob/${SOURCE.sha}/README.md#connect" target="_blank" rel="noopener noreferrer">Connection guide ${icon("arrow-up-right")}</a></div></section><details class="technical-details"><summary>Device & connection notes</summary><p>${devices[state.device].note}</p><p>${modes[state.mode].note}</p><p>Pairings are remembered until removed or browser data is cleared. Changing between the local HTTP controller and the hosted HTTPS controller uses separate browser storage; pair again if prompted. If an identity check asks for fresh pairing, scan a new QR from your PC.</p><p>This guide follows repository main at ${SOURCE.sha.slice(0, 7)}, checked ${SOURCE.date}. Installed release builds may differ. Browser-specific features are checked by the actual controller, not by your selection here.</p></details><div class="detail-source"><a href="${SOURCE.base}/blob/${SOURCE.sha}/docs/features.md#${f.source}" target="_blank" rel="noopener noreferrer">Read feature documentation ${icon("arrow-up-right")}</a><span>Source: 95d1816</span></div>`;
}
function openFeature(id) {
  if (!features.some((f) => f.id === id)) return;
  selected = id;
  renderDetail();
  if (!$("#feature-dialog").open) $("#feature-dialog").showModal();
  setUrl(true);
  $("#feature-dialog .close-dialog").focus();
}
function renderComparison() {
  const rows = [
    ["Where it works", "Same trusted LAN", "Same private LAN", "Across networks"],
    ["Internet needed", "No", "App loading + setup", "Throughout"],
    ["Established traffic", "Local network", "Local network", "Encrypted relay"],
    ["Gyro & phone webcam", "Not on local HTTP", "With device support", "With device support"],
    [
      "Device-local saving",
      "No secure storage APIs",
      "With browser support",
      "With browser support",
    ],
    ["Incoming PC connection", "Yes", "Yes, local network", "No"],
    ["Shared relay allowance", "No", "No", "Yes, media / TURN"],
  ];
  $("#comparison-content").innerHTML = `<div class="comparison-cards">${Object.entries(modes)
    .map(
      ([id, m]) =>
        `<div class="comparison-card ${id === state.mode ? "active" : ""}">${icon(m.icon)}<h3>${m.label}</h3><p>${m.description}</p><button class="${id === state.mode ? "primary" : "secondary"}-button" data-compare-mode="${id}">${id === state.mode ? "Keep " + m.label : "Use " + m.label}</button></div>`,
    )
    .join(
      "",
    )}</div><div class="comparison-table"><table><thead><tr><th scope="col">What changes</th><th scope="col">Standard Local</th><th scope="col">Enhanced Direct</th><th scope="col">Cloud Relay</th></tr></thead><tbody>${rows
    .map(
      (row) =>
        `<tr><th scope="row">${row[0]}</th>${row
          .slice(1)
          .map((c) => `<td>${c}</td>`)
          .join("")}</tr>`,
    )
    .join(
      "",
    )}</tbody></table></div><p class="comparison-note">All three routes require a running Windows 11 host and pairing. Enhanced Direct still needs LAN reachability. For Relay, check the PC’s Connection page for current service-wide media usage. Nothing switches automatically if a route fails.</p>`;
}
async function copy(text, container) {
  try {
    await navigator.clipboard.writeText(text);
    notify("Copied to clipboard");
  } catch {
    container.querySelector(".copy-fallback")?.remove();
    const box = document.createElement("textarea");
    box.className = "copy-fallback";
    box.setAttribute("aria-label", "Copy this text");
    box.value = text;
    box.readOnly = true;
    container.append(box);
    box.focus();
    box.select();
    notify("Select and copy the text shown below.");
  }
}
function guideText() {
  const f = features.find((x) => x.id === selected);
  const r = evaluate(f, state);
  return `${f.title} — Voltura Air\n${devices[state.device].label} · ${modes[state.mode].label} · ${profileLabel()}\n\n${r.label}\n${[...r.blockers.map((b) => b.text), ...r.checks].join("\n")}\n\nCapabilities\n${f.capabilities.map((s) => "• " + s).join("\n")}\n\nRequired permissions: ${f.permissions.map((p) => permissions[p]).join(", ") || (f.myOnly ? "My device profile" : "Per-action permissions")}\n${Object.entries(
    f.optional || {},
  )
    .map(([k, v]) => "Optional: " + permissions[k] + " — " + v)
    .join(
      "\n",
    )}\n\nLimitations\n${[...f.limits, ...(relayLimit(f) ? [relayLimit(f)] : [])].map((s) => "• " + s).join("\n")}\n\nSetup\n${setupSteps(
    f,
    state,
  )
    .map((s, i) => `${i + 1}. ${s.title}\n${s.text}`)
    .join("\n\n")}\n\n${SOURCE.base}/blob/${SOURCE.sha}/docs/features.md#${f.source}`;
}
document.addEventListener("click", (e) => {
  const btn = e.target.closest("button");
  if (!btn) return;
  if (btn.dataset.family) {
    const d =
      btn.dataset.family === "Phone"
        ? "iphone"
        : btn.dataset.family === "Tablet"
          ? "ipad"
          : "computer";
    update({ device: d }, `[data-family="${btn.dataset.family}"]`);
  }
  if (btn.dataset.mode) update({ mode: btn.dataset.mode }, `[data-mode="${btn.dataset.mode}"]`);
  if (btn.dataset.goal) update({ goal: btn.dataset.goal }, `[data-goal="${btn.dataset.goal}"]`);
  if (btn.dataset.feature) openFeature(btn.dataset.feature);
  if (btn.dataset.compareMode) {
    update({ mode: btn.dataset.compareMode });
    $("#compare-dialog").close();
  }
  if (btn.dataset.fix) {
    update(
      btn.dataset.fix === "https"
        ? { mode: "enhanced" }
        : btn.dataset.fix === "device"
          ? { device: "iphone" }
          : { profile: "my" },
    );
    $("#feature-title")?.focus();
    notify("Explorer selection updated. Apply the matching change on your PC.");
  }
  if (btn.classList.contains("close-dialog")) {
    if (btn.closest("dialog").id === "feature-dialog") {
      selected = null;
      setUrl();
    }
    btn.closest("dialog").close();
  }
  if (btn.id === "clear-filters") {
    const noMatches = getFeatures(state, $("#search").value).length === 0;
    $("#hide-blocked").checked = false;
    if (noMatches) {
      $("#search").value = "";
      state.goal = "all";
    }
    render();
    setUrl();
    $("#search").focus();
  }
  if (btn.id === "copy-guide") copy(guideText(), $("#feature-detail"));
});
$("#device-detail").addEventListener("change", (e) =>
  update({ device: e.target.value }, "#device-detail"),
);
$("#profile").addEventListener("change", (e) => update({ profile: e.target.value }, "#profile"));
$("#search").addEventListener("input", () => {
  renderResults();
  setUrl();
});
$("#hide-blocked").addEventListener("change", () => {
  renderResults();
  setUrl();
});
$("#reset").addEventListener("click", () => {
  $("#search").value = "";
  $("#hide-blocked").checked = false;
  state = cleanState();
  render();
  setUrl(true);
  $("#reset").focus();
  notify("Setup reset to iPhone · Enhanced Direct · My device");
});
$("#show-results").addEventListener("click", () => {
  $("#results").scrollIntoView({
    behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
    block: "start",
  });
  $("#results").focus({ preventScroll: true });
});
$("#compare").addEventListener("click", () => {
  renderComparison();
  $("#compare-dialog").showModal();
  $("#compare-dialog .close-dialog").focus();
});
$("#copy-config").addEventListener("click", () => {
  setUrl();
  copy(location.href, $("#results"));
});
$("#feature-dialog").addEventListener("cancel", () => {
  selected = null;
  setUrl();
});
$("#feature-dialog").addEventListener("close", () => {
  if (selected) {
    selected = null;
    setUrl();
  }
});
for (const d of document.querySelectorAll("dialog"))
  d.addEventListener("click", (e) => {
    if (e.target === d) {
      const r = d.getBoundingClientRect();
      if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom)
        d.close();
    }
  });
window.addEventListener("popstate", () => {
  const p = new URLSearchParams(location.search);
  state = cleanState(Object.fromEntries(p));
  $("#search").value = (p.get("q") || "").slice(0, 200);
  $("#hide-blocked").checked = p.get("ready") === "1";
  const f = p.get("feature");
  if (f && features.some((x) => x.id === f)) {
    selected = f;
    render();
    renderDetail();
    if (!$("#feature-dialog").open) $("#feature-dialog").showModal();
  } else {
    selected = null;
    if ($("#feature-dialog").open) $("#feature-dialog").close();
    render();
  }
});
function setTheme(theme) {
  document.documentElement.classList.add("theme-changing");
  document.documentElement.dataset.theme = theme;
  $("#theme").innerHTML = icon(theme === "dark" ? "sun" : "moon");
  $("#theme").setAttribute(
    "aria-label",
    "Switch to " + (theme === "dark" ? "light" : "dark") + " theme",
  );
  try {
    localStorage.setItem("voltura-explorer-theme", theme);
  } catch {}
  void document.documentElement.offsetHeight;
  requestAnimationFrame(() => document.documentElement.classList.remove("theme-changing"));
}
$("#theme").addEventListener("click", () =>
  setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark"),
);
try {
  const t = localStorage.getItem("voltura-explorer-theme");
  if (["dark", "light"].includes(t)) setTheme(t);
} catch {}
hydrate();
render();
const initialFeature = params.get("feature");
if (initialFeature && features.some((f) => f.id === initialFeature)) {
  selected = initialFeature;
  renderDetail();
  $("#feature-dialog").showModal();
}
setUrl();
