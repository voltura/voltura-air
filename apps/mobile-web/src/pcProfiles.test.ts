import { describe, expect, it } from "vitest";
import {
  addPcProfile,
  createPcProfile,
  forgetPcProfile,
  normalizePcProfile,
  renamePcProfile,
  selectPcProfile,
  upsertPcProfile
} from "./pcProfiles";

describe("pcProfiles", () => {
  it("normalizes a URL into an origin-based profile", () => {
    expect(createPcProfile("http://192.168.1.50:51395/path?x=1")).toEqual({
      customName: false,
      id: "http://192.168.1.50:51395",
      name: "PC",
      url: "http://192.168.1.50:51395"
    });
  });

  it("normalizes stored profiles and preserves custom names", () => {
    expect(
      normalizePcProfile({
        customName: true,
        name: "Living room PC",
        url: "http://192.168.1.50:51395/ws"
      })
    ).toEqual({
      customName: true,
      id: "http://192.168.1.50:51395",
      name: "Living room PC",
      url: "http://192.168.1.50:51395"
    });
  });

  it("falls back to PC for non-custom IP-like stored names", () => {
    expect(
      normalizePcProfile({
        customName: false,
        name: "192.168.1.50",
        url: "http://192.168.1.50:51395"
      })?.name
    ).toBe("PC");
  });

  it("adds a manually entered PC profile", () => {
    const profiles = addPcProfile([], "http://192.168.1.50:51395");

    expect(profiles).toHaveLength(1);
    expect(profiles[0].id).toBe("http://192.168.1.50:51395");
  });

  it("upserts an existing PC without overwriting its custom name", () => {
    const existing = {
      ...createPcProfile("http://192.168.1.50:51395"),
      customName: true,
      name: "Sofa PC"
    };

    const profiles = upsertPcProfile([existing], createPcProfile("http://192.168.1.50:51395/new"));

    expect(profiles).toHaveLength(1);
    expect(profiles[0].customName).toBe(true);
    expect(profiles[0].name).toBe("Sofa PC");
    expect(profiles[0].url).toBe("http://192.168.1.50:51395");
  });

  it("selects an existing PC profile", () => {
    const profile = createPcProfile("http://192.168.1.50:51395");

    expect(selectPcProfile([profile], profile.id)).toBe(profile);
    expect(selectPcProfile([profile], "missing")).toBeNull();
  });

  it("renames a saved PC profile", () => {
    const profile = createPcProfile("http://192.168.1.50:51395");
    const profiles = renamePcProfile([profile], profile.id, "TV PC");

    expect(profiles[0]).toMatchObject({
      customName: true,
      name: "TV PC"
    });
  });

  it("forgets a non-active saved PC profile and keeps the active profile", () => {
    const active = createPcProfile("http://192.168.1.50:51395");
    const other = createPcProfile("http://192.168.1.51:51395");

    const result = forgetPcProfile([active, other], active.id, other.id);

    expect(result.profiles).toEqual([active]);
    expect(result.activePcId).toBe(active.id);
  });

  it("clears active profile when deleting the active saved PC", () => {
    const active = createPcProfile("http://192.168.1.50:51395");
    const other = createPcProfile("http://192.168.1.51:51395");

    const result = forgetPcProfile([active, other], active.id, active.id);

    expect(result.profiles).toEqual([other]);
    expect(result.activePcId).toBeNull();
  });
});
