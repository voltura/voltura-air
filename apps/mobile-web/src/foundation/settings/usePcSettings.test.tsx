import { act, renderHook } from "@testing-library/react";
import { expect, it } from "vitest";
import { keyboardSettingsKey } from "./appStorage";
import { defaultKeyboardSettings } from "./keyboardSettings";
import { usePcSettings } from "./usePcSettings";

it("loads the new client's keyboard preferences without overwriting them with the previous client", () => {
  const a = "settings-scope-a",
    b = "settings-scope-b";
  const first = { ...defaultKeyboardSettings, showArrowKeys: false };
  const second = { ...defaultKeyboardSettings, showFunctionKeys: true };
  localStorage.setItem(keyboardSettingsKey(a), JSON.stringify(first));
  localStorage.setItem(keyboardSettingsKey(b), JSON.stringify(second));
  const hook = renderHook(({ client }) => usePcSettings(client, null), {
    initialProps: { client: a },
  });
  hook.rerender({ client: b });
  expect(hook.result.current.keyboardSettings).toEqual(second);
  expect(JSON.parse(localStorage.getItem(keyboardSettingsKey(b))!)).toEqual(second);
  act(() =>
    hook.result.current.setKeyboardSettings((current) => ({ ...current, showSleepButton: false })),
  );
  expect(JSON.parse(localStorage.getItem(keyboardSettingsKey(a))!)).toEqual(first);
});
