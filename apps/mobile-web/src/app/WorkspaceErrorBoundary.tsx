import { Component, type ReactNode } from "react";

interface Props {
  children: ReactNode;
  featureName: string;
  onBack: () => void;
}

interface State {
  failed: boolean;
}

export class WorkspaceErrorBoundary extends Component<Props, State> {
  public state: State = { failed: false };

  public static getDerivedStateFromError(): State {
    return { failed: true };
  }

  public render() {
    if (this.state.failed) {
      return (
        <section className="workspace-error" role="alert">
          <strong>{this.props.featureName} could not open</strong>
          <span>Return to the app and try again.</span>
          <button type="button" onClick={this.props.onBack}>
            Back
          </button>
        </section>
      );
    }

    return this.props.children;
  }
}
