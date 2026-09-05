import { useEffect, useMemo, useRef, useState } from "react";
import { Activity, ChevronLeft, Clipboard, RefreshCw } from "lucide-react";
import type {
  MobileHostDiagnosticsSnapshot,
  ScreenViewSoundQuality,
} from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import { getBrowserName, getDisplayMode } from "../../foundation/platform/clientEnvironment";
import type { DiagnosticsFailure } from "../../foundation/connection/useDiagnostics";
import "./diagnostics.css";

export interface DiagnosticRow {
  label: string;
  value: string;
}

export interface DiagnosticGroup {
  title: "Voltura Air" | "Connection" | "Computer";
  rows: DiagnosticRow[];
}

interface DiagnosticsWorkspaceProps {
  state: ConnectionState;
  permission: boolean | undefined;
  snapshot: MobileHostDiagnosticsSnapshot | null;
  screenSoundQuality?: ScreenViewSoundQuality | undefined;
  pending: boolean;
  failure: DiagnosticsFailure | null;
  requestDiagnostics: () => string | null;
  onBack: () => void;
  onCopyFeedback: (message: string, tone: "success" | "error") => void;
}

export function DiagnosticsWorkspace(props: DiagnosticsWorkspaceProps) {
  const { pending, permission, requestDiagnostics, state } = props;
  const [manualCopy, setManualCopy] = useState("");
  const initialRequestSentRef = useRef(false);

  useEffect(() => {
    if (state !== "paired" || permission !== true) {
      initialRequestSentRef.current = false;
      return;
    }
    if (!initialRequestSentRef.current) {
      initialRequestSentRef.current = true;
      if (!pending) {
        requestDiagnostics();
      }
    }
  }, [pending, permission, requestDiagnostics, state]);

  const groups = useMemo(
    () => buildDiagnosticsGroups(props.state, props.snapshot, props.screenSoundQuality),
    [props.screenSoundQuality, props.snapshot, props.state],
  );
  const rows = groups.flatMap((group) => group.rows);
  const copy = async (text: string, success: string) => {
    setManualCopy("");
    if ((await copyTextToClipboard(text)) === "copied") {
      props.onCopyFeedback(success, "success");
      return;
    }

    setManualCopy(text);
    props.onCopyFeedback(
      "Could not copy automatically. Select the text below and copy it manually.",
      "error",
    );
  };

  const generated = props.snapshot
    ? formatGeneratedAt(props.snapshot.generatedAt)
    : "Host snapshot not generated";
  const unavailableState = getUnavailableState(props);

  return (
    <section
      className="diagnostics-workspace"
      aria-labelledby="diagnostics-title"
      aria-busy={props.pending}
    >
      <header className="diagnostics-header">
        <button type="button" className="diagnostics-back" onClick={props.onBack}>
          <ChevronLeft aria-hidden="true" />
          <span>Back</span>
        </button>
        <div className="diagnostics-heading">
          <span className="diagnostics-eyebrow">SUPPORT</span>
          <h1 id="diagnostics-title">Diagnostics</h1>
        </div>
        <Activity aria-hidden="true" className="diagnostics-icon" />
      </header>

      <div className="diagnostics-scroll-region">
        <div className="diagnostics-snapshot-bar">
          <div>
            <span>Generated</span>
            <strong>{generated}</strong>
          </div>
          <button
            type="button"
            onClick={props.requestDiagnostics}
            disabled={props.state !== "paired" || props.permission !== true || props.pending}
          >
            <RefreshCw
              aria-hidden="true"
              className={props.pending ? "diagnostics-refreshing" : undefined}
            />
            <span>Refresh</span>
          </button>
        </div>

        {unavailableState && (
          <div
            className={`diagnostics-state diagnostics-state-${unavailableState.tone}`}
            role={unavailableState.tone === "error" ? "alert" : "status"}
          >
            <p>{unavailableState.message}</p>
            {unavailableState.retry && (
              <button type="button" onClick={props.requestDiagnostics}>
                Retry
              </button>
            )}
          </div>
        )}

        {groups.map((group) => (
          <section
            className="diagnostics-group"
            aria-labelledby={`diagnostics-${group.title.toLowerCase().replaceAll(" ", "-")}`}
            key={group.title}
          >
            <h2 id={`diagnostics-${group.title.toLowerCase().replaceAll(" ", "-")}`}>
              {group.title}
            </h2>
            <div className="diagnostics-rows">
              {group.rows.map((row) => {
                const line = formatDiagnosticRow(row);
                return (
                  <div className="diagnostics-row" key={row.label}>
                    <div>
                      <span>{row.label}</span>
                      <strong className="selectable-text">{row.value}</strong>
                    </div>
                    <button
                      type="button"
                      className="icon-button"
                      aria-label={`Copy ${row.label}`}
                      onClick={() => {
                        void copy(line, `${row.label} copied.`);
                      }}
                    >
                      <Clipboard aria-hidden="true" />
                    </button>
                  </div>
                );
              })}
            </div>
          </section>
        ))}

        <div className="diagnostics-copy-all">
          <button
            type="button"
            onClick={() => {
              void copy(buildDiagnosticsText(rows), "Diagnostics copied.");
            }}
          >
            <Clipboard aria-hidden="true" />
            <span>Copy all</span>
          </button>
          {manualCopy && (
            <textarea
              aria-label="Diagnostics text"
              className="text-input selectable-text"
              onFocus={(event) => {
                event.currentTarget.select();
              }}
              readOnly
              rows={8}
              value={manualCopy}
            />
          )}
        </div>
      </div>
    </section>
  );
}

export function buildDiagnosticsGroups(
  state: ConnectionState,
  snapshot: MobileHostDiagnosticsSnapshot | null,
  screenSoundQuality?: ScreenViewSoundQuality,
): DiagnosticGroup[] {
  const groups: DiagnosticGroup[] = [
    {
      title: "Voltura Air",
      rows: [
        {
          label: "Web app",
          value:
            import.meta.env.BASE_URL === "/air/dev-app/"
              ? "Development"
              : import.meta.env.BASE_URL === "/air/app/"
                ? "Stable"
                : "PC-hosted",
        },
        { label: "Web client version", value: __APP_VERSION__ },
        ...(snapshot ? [{ label: "Host version", value: snapshot.hostVersion }] : []),
        ...(screenSoundQuality
          ? [{ label: "Sound quality", value: displaySoundQuality(screenSoundQuality) }]
          : []),
      ],
    },
    {
      title: "Connection",
      rows: [
        { label: "Connection state", value: state },
        { label: "Browser", value: getBrowserName() },
        { label: "Display mode", value: getDisplayMode() },
        ...(snapshot ? buildHostConnectionRows(snapshot) : []),
      ],
    },
    {
      title: "Computer",
      rows: snapshot
        ? [
            { label: "Windows", value: snapshot.computer.windows },
            { label: "System", value: snapshot.computer.system },
            { label: "Processor", value: snapshot.computer.processor },
            { label: "Logical processors", value: snapshot.computer.logicalProcessors },
            { label: "Primary display", value: snapshot.computer.primaryDisplay },
            { label: "Installed memory", value: snapshot.computer.installedMemory },
            { label: "Available memory", value: snapshot.computer.availableMemory },
            { label: "System disk", value: snapshot.computer.systemDisk },
            { label: "System uptime", value: snapshot.computer.systemUptime },
          ]
        : [],
    },
  ];

  return groups.filter((group) => group.rows.length > 0);
}

function displaySoundQuality(soundQuality: ScreenViewSoundQuality): string {
  return soundQuality.charAt(0).toUpperCase() + soundQuality.slice(1);
}

function buildHostConnectionRows(snapshot: MobileHostDiagnosticsSnapshot): DiagnosticRow[] {
  return [
    { label: "Connection method", value: snapshot.connectionMethod },
    { label: "Enhanced capabilities", value: snapshot.enhancedCapabilities },
    { label: "Relay status", value: snapshot.relayStatus },
    { label: "Relay endpoint type", value: snapshot.relayEndpointType },
    { label: "Relay failure code", value: snapshot.relayFailureCode },
    { label: "Pairing state", value: snapshot.pairingState },
    { label: "Windows lock policy", value: snapshot.windowsLockPolicy },
    { label: "Application logging", value: snapshot.applicationLogging },
    { label: "Application log retention", value: snapshot.applicationLogRetention },
    { label: "Paired device count", value: String(snapshot.pairedDeviceCount) },
    { label: "Connected device count", value: String(snapshot.connectedDeviceCount) },
    { label: "PC name", value: snapshot.pcName },
    { label: "Selected adapter", value: snapshot.selectedAdapter },
    { label: "Selected IP", value: snapshot.selectedIp },
    { label: "Selected port", value: String(snapshot.selectedPort) },
    ...snapshot.advisories.flatMap((advisory) => [
      { label: advisory.name, value: advisory.summary },
      { label: `${advisory.name} details`, value: advisory.details },
      { label: `${advisory.name} code`, value: advisory.code },
    ]),
  ];
}

export function formatDiagnosticRow(row: DiagnosticRow): string {
  return `${row.label}: ${row.value}`;
}

export function buildDiagnosticsText(rows: readonly DiagnosticRow[]): string {
  return rows.map(formatDiagnosticRow).join("\n");
}

function formatGeneratedAt(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function getUnavailableState(
  props: DiagnosticsWorkspaceProps,
): { message: string; tone: "neutral" | "error"; retry: boolean } | null {
  if (props.state !== "paired") {
    return {
      message: "Connect to a PC to include host and computer information.",
      tone: "neutral",
      retry: false,
    };
  }
  if (props.permission === false) {
    return {
      message:
        "View diagnostics is blocked for this device. Change its access profile on the PC to allow it.",
      tone: "neutral",
      retry: false,
    };
  }
  if (props.permission === undefined) {
    return {
      message: "This PC does not support mobile diagnostics.",
      tone: "neutral",
      retry: false,
    };
  }
  if (props.failure) {
    return { message: props.failure.message, tone: "error", retry: true };
  }
  if (props.pending && props.snapshot === null) {
    return { message: "Loading PC diagnostics…", tone: "neutral", retry: false };
  }
  return null;
}
