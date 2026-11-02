import { lazy, Suspense } from "react";
import type { IntroductionProps } from "./Introduction";
const Guide = lazy(() =>
  import("./Introduction").then((module) => ({ default: module.Introduction })),
);

export function Introduction(props: IntroductionProps) {
  if (!props.eligible && props.replay === 0) {
    return null;
  }
  return (
    <Suspense fallback={null}>
      <Guide {...props} />
    </Suspense>
  );
}
