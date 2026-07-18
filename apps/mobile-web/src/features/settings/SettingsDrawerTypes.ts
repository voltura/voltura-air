import type { ChangeEvent, ComponentType, Dispatch, RefObject, SetStateAction } from "react";
import type { AppSettings, FourthMode } from "../../foundation/settings/appSettings";
import type { TrackpadSettings } from "../../foundation/input/gestures";
import type { KeyboardSettings } from "../../foundation/settings/keyboardSettings";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import type { ManualConnectionTarget } from "../../foundation/pairing/pairingLink";
import type { RemoteSettings } from "../../foundation/settings/remoteSettings";

export type ThemeMode = "system" | "light" | "dark";
export type SettingsSection = "connection" | "custom-pointer" | "trackpad" | "keyboard" | "split" | "remote" | "appearance" | "app";

export interface SettingsToolOption {
  id: FourthMode;
  label: string;
  Icon: ComponentType<{ "aria-hidden"?: "true" }>;
}

export interface SettingsDrawerProps {
  activePc: PcProfile | null;
  appSettings: AppSettings;
  diagnostics: string;
  deviceName: string;
  customPointerEnabled?: boolean | undefined;
  disconnectActivePc: () => void;
  forgetPc: (pcId: string) => void;
  installApp: () => Promise<void>;
  installPrompt: Event | null;
  isInstalled: boolean;
  isOpen: boolean;
  keyboardSettings: KeyboardSettings;
  onClose: () => void;
  onPairingQrSelected: (event: ChangeEvent<HTMLInputElement>) => Promise<void>;
  onManualHostSubmit: (target: ManualConnectionTarget) => void;
  onOpenGestureDebug?: (() => void) | undefined;
  onOpenTool?: (tool: FourthMode) => void;
  pairedPcs: PcProfile[];
  pairingQrInputRef: RefObject<HTMLInputElement | null>;
  pairingScanMessage: string;
  presentationAvailable: boolean;
  refreshInstalledApp: () => Promise<void>;
  refreshMessage: string;
  renameDevice: (name: string) => void;
  renamePc: (pcId: string, name: string) => void;
  remoteSettings: RemoteSettings;
  scanPairingQr: () => void;
  selectPc: (pcId: string) => void;
  setHostCustomPointer?: ((enabled: boolean) => void) | undefined;
  setThemeMode: Dispatch<SetStateAction<ThemeMode>>;
  showGestureDebug: boolean;
  supportsRemoteLaunch: boolean;
  themeMode: ThemeMode;
  toolOptions: readonly SettingsToolOption[];
  trackpadSettings: TrackpadSettings;
  updateAppSetting: <Key extends keyof AppSettings>(key: Key, value: AppSettings[Key]) => void;
  updateKeyboardSetting: <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => void;
  updateRemoteSetting: <Key extends keyof RemoteSettings>(key: Key, value: RemoteSettings[Key]) => void;
  updateTrackpadSetting: <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => void;
}
