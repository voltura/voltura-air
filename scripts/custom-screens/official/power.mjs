import { screen, button, buttonGrid } from "../builders/screen-builder.mjs";
import { hostAction } from "../builders/actions.mjs";
const id = "official.power";
export default screen({
  id,
  name: "Power",
  revision: "official-power-1",
  category: "Windows",
  tags: ["Windows", "Power", "System"],
  shortDescription: "Permissioned Windows lock, sleep, hibernate, restart, and shutdown controls.",
  sections: [
    buttonGrid(`${id}.controls`, "Power controls", [
      button(`${id}.lock`, "Lock PC", hostAction("power.lock"), { icon: "monitor" }),
      button(`${id}.sleep`, "Sleep", hostAction("power.sleep"), { icon: "monitor" }),
      button(`${id}.hibernate`, "Hibernate", hostAction("power.hibernate"), { icon: "monitor" }),
      button(`${id}.restart`, "Restart", hostAction("power.restart"), {
        icon: "refresh",
        size: "wide",
      }),
      button(`${id}.shutdown`, "Shut down", hostAction("power.shutdown"), {
        icon: "square-x",
        size: "wide",
      }),
    ]),
  ],
});
