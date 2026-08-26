import { describe, expect, it } from "vitest";
import { defaultTrackpadSettings, GestureRecognizer, normalizeTrackpadSettings } from "./gestures";

describe("GestureRecognizer", () => {
  it("turns one-finger movement into pointer movement", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);
    const events = recognizer.move([{ id: 1, x: 110, y: 96 }], 20);

    expect(events).toEqual([{ type: "pointer.move", dx: 13.5, dy: -5.4 }]);
  });

  it("applies pointer speed", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);
    const events = recognizer.move([{ id: 1, x: 110, y: 96 }], 20, {
      ...defaultTrackpadSettings,
      pointerSpeed: 50,
    });

    expect(events).toEqual([{ type: "pointer.move", dx: 6.75, dy: -2.7 }]);
  });

  it("can smooth pointer movement", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);
    expect(
      recognizer.move([{ id: 1, x: 110, y: 100 }], 20, {
        ...defaultTrackpadSettings,
        pointerSmoothing: true,
      }),
    ).toEqual([{ type: "pointer.move", dx: 13.5, dy: 0 }]);

    expect(
      recognizer.move([{ id: 1, x: 110, y: 110 }], 40, {
        ...defaultTrackpadSettings,
        pointerSmoothing: true,
      }),
    ).toEqual([{ type: "pointer.move", dx: 8.78, dy: 4.73 }]);
  });

  it("can accelerate faster pointer movement", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);
    const events = recognizer.move([{ id: 1, x: 120, y: 100 }], 20, {
      ...defaultTrackpadSettings,
      pointerAcceleration: true,
    });

    expect(events).toEqual([{ type: "pointer.move", dx: 35.68, dy: 0 }]);
  });

  it("turns a short one-finger tap into a left click", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);

    expect(recognizer.end(120)).toEqual([
      { type: "pointer.button", button: "left", action: "click" },
    ]);
  });

  it("turns a long press into a right click", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);

    expect(recognizer.end(700)).toEqual([
      { type: "pointer.button", button: "right", action: "click" },
    ]);
  });

  it("turns two-finger movement into vertical and horizontal wheel events", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 92, y: 120 },
        { id: 2, x: 152, y: 120 },
      ],
      20,
    );

    expect(events).toEqual([{ type: "pointer.wheel", dx: 8.8, dy: -22 }]);
  });

  it("turns a two-finger spread into zoom in", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 97, y: 100 },
        { id: 2, x: 163, y: 100 },
      ],
      20,
      { ...defaultTrackpadSettings, zoomGestures: true },
      "zoom",
    );

    expect(events).toEqual([{ type: "pointer.zoom", direction: "in" }]);
  });

  it("turns a two-finger pinch into zoom out", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 103, y: 100 },
        { id: 2, x: 157, y: 100 },
      ],
      20,
      { ...defaultTrackpadSettings, zoomGestures: true },
      "zoom",
    );

    expect(events).toEqual([{ type: "pointer.zoom", direction: "out" }]);
  });

  it("keeps two-finger scrolling when zoom gestures are enabled and finger spacing wobbles", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 94, y: 120 },
        { id: 2, x: 166, y: 120 },
      ],
      20,
      { ...defaultTrackpadSettings, zoomGestures: true },
      "scroll",
    );

    expect(events).toEqual([{ type: "pointer.wheel", dx: 0, dy: -22 }]);
  });

  it("keeps spacing changes in explicit Scroll mode", () => {
    const recognizer = new GestureRecognizer();
    const settings = { ...defaultTrackpadSettings, zoomGestures: true };

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    expect(
      recognizer.move(
        [
          { id: 1, x: 100, y: 120 },
          { id: 2, x: 160, y: 120 },
        ],
        20,
        settings,
        "scroll",
      ),
    ).toEqual([{ type: "pointer.wheel", dx: 0, dy: -22 }]);

    expect(
      recognizer.move(
        [
          { id: 1, x: 90, y: 130 },
          { id: 2, x: 170, y: 130 },
        ],
        40,
        settings,
        "scroll",
      ),
    ).toEqual([{ type: "pointer.wheel", dx: 0, dy: -11 }]);
  });

  it("keeps shared movement out of explicit Zoom mode", () => {
    const recognizer = new GestureRecognizer();
    const settings = { ...defaultTrackpadSettings, zoomGestures: true };

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    expect(
      recognizer.move(
        [
          { id: 1, x: 90, y: 100 },
          { id: 2, x: 170, y: 100 },
        ],
        20,
        settings,
        "zoom",
      ),
    ).toEqual([{ type: "pointer.zoom", direction: "in" }]);

    expect(
      recognizer.move(
        [
          { id: 1, x: 90, y: 125 },
          { id: 2, x: 170, y: 125 },
        ],
        40,
        settings,
        "zoom",
      ),
    ).toEqual([]);
  });

  it("does not emit zoom events when zoom gestures are disabled", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 95, y: 100 },
        { id: 2, x: 165, y: 100 },
      ],
      20,
      { ...defaultTrackpadSettings, zoomGestures: false },
      "zoom",
    );

    expect(events).toEqual([]);
  });

  it("honors scroll axis and direction settings", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 92, y: 120 },
        { id: 2, x: 152, y: 120 },
      ],
      20,
      { ...defaultTrackpadSettings, horizontalScroll: false, scrollDirection: "inverted" },
    );

    expect(events).toEqual([{ type: "pointer.wheel", dx: 0, dy: 22 }]);
  });

  it("can accelerate faster scroll movement", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );
    const events = recognizer.move(
      [
        { id: 1, x: 100, y: 132 },
        { id: 2, x: 160, y: 132 },
      ],
      20,
      { ...defaultTrackpadSettings, scrollAcceleration: true },
    );

    expect(events).toEqual([{ type: "pointer.wheel", dx: 0, dy: -59.84 }]);
  });

  it("turns a two-finger tap into a right click", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start(
      [
        { id: 1, x: 100, y: 100 },
        { id: 2, x: 160, y: 100 },
      ],
      0,
    );

    expect(recognizer.end(140)).toEqual([
      { type: "pointer.button", button: "right", action: "click" },
    ]);
  });

  it("can disable tap to click", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);

    expect(recognizer.end(120, { ...defaultTrackpadSettings, tapToClick: false })).toEqual([]);
  });

  it("cancels accidental touch-count changes", () => {
    const recognizer = new GestureRecognizer();

    recognizer.start([{ id: 1, x: 100, y: 100 }], 0);
    expect(
      recognizer.move(
        [
          { id: 1, x: 100, y: 100 },
          { id: 2, x: 120, y: 120 },
        ],
        20,
      ),
    ).toEqual([]);

    expect(recognizer.end(100)).toEqual([]);
  });
});

describe("normalizeTrackpadSettings", () => {
  it("fills omitted settings from the defaults", () => {
    expect(normalizeTrackpadSettings({ pointerSpeed: 40 })).toEqual({
      ...defaultTrackpadSettings,
      pointerSpeed: 40,
      showVolumeControl: true,
      enableSplitMode: false,
    });
  });

  it("preserves disabled volume control setting", () => {
    expect(normalizeTrackpadSettings({ showVolumeControl: false }).showVolumeControl).toBe(false);
  });

  it("preserves enabled split mode setting", () => {
    expect(normalizeTrackpadSettings({ enableSplitMode: true }).enableSplitMode).toBe(true);
  });

  it("normalizes gyro sensitivity to its persisted range and step", () => {
    expect(normalizeTrackpadSettings({ gyroSensitivity: 133 }).gyroSensitivity).toBe(135);
    expect(normalizeTrackpadSettings({ gyroSensitivity: 500 }).gyroSensitivity).toBe(200);
    expect(normalizeTrackpadSettings({ gyroSensitivity: -10 }).gyroSensitivity).toBe(25);
  });

  it("preserves the current split layout preferences while discarding the replaced mode-button setting", () => {
    expect(
      normalizeTrackpadSettings({
        splitTrackpadPlacement: "left",
        splitShowStatusRow: true,
      }),
    ).toMatchObject({
      splitTrackpadPlacement: "left",
      splitShowStatusRow: true,
    });
  });
});
