const defaultLayout = (order) => ({ order, visible: true });

export function button(id, label, action, options = {}) {
  return {
    id,
    name: options.name ?? label,
    label,
    icon: options.icon ?? "command",
    presentation: options.presentation ??
      (action.kind === "text" || action.kind === "shortcut" ? "label" : "iconLabel"),
    size: options.size ?? "standard",
    repeat: options.repeat ?? false,
    portrait: options.portrait ?? null,
    landscape: options.landscape ?? null,
    action,
    row: options.row ?? 0
  };
}

export function buttonGrid(id, name, buttons, options = {}) {
  return {
    id,
    name,
    showHeader: options.showHeader ?? true,
    widthColumns: options.widthColumns ?? 12,
    heightMode: options.heightMode ?? "content",
    fillWeight: options.fillWeight ?? 1,
    rowLimit: options.rowLimit ?? 0,
    portrait: options.portrait ?? null,
    landscape: options.landscape ?? null,
    buttons,
    kind: options.collapsible ? "collapsible" : "buttons",
    trackpadLeftClick: true,
    trackpadRightClick: true,
    trackpadButtonSide: "right",
    initiallyExpanded: options.initiallyExpanded ?? true,
    trackpadFullscreenControl: false,
    trackpadGyroControl: false,
    buttonAlignment: options.buttonAlignment ?? "space-evenly"
  };
}

export function volumeControls(id, options = {}) {
  return {
    ...buttonGrid(id, options.name ?? "Volume", [], options),
    showHeader: options.showHeader ?? false,
    kind: "volume",
    widthColumns: options.widthColumns ?? 12
  };
}

export function navigationPad(id, options = {}) {
  return {
    ...buttonGrid(id, options.name ?? "Navigation", [], options),
    showHeader: options.showHeader ?? false,
    kind: options.kind ?? "dpad",
    widthColumns: options.widthColumns ?? 12
  };
}

export function trackpad(id, options = {}) {
  return {
    ...buttonGrid(id, options.name ?? "Trackpad", [], options),
    kind: options.collapsible ? "collapsibleTrackpad" : "trackpad",
    heightMode: options.heightMode ?? "fill",
    trackpadFullscreenControl: options.fullscreen ?? true,
    trackpadGyroControl: options.gyro ?? false
  };
}

export function portraitLandscape(order, landscape = {}) {
  return {
    portrait: defaultLayout(order),
    landscape: { ...defaultLayout(order), ...landscape }
  };
}
