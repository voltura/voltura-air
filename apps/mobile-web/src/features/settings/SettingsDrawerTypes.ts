import type { ChangeEvent, ComponentType, Dispatch, RefObject, SetStateAction } from "react";
import type { AppSettings, FourthMode } from "../../foundation/settings/appSettings";
import type { TrackpadSettings } from "../../foundation/input/gestures";
import type { KeyboardSettings } from "../../foundation/settings/keyboardSettings";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import type { ManualConnectionTarget } from "../../foundation/pairing/pairingLink";
import type { RemoteSettings } from "../../foundation/settings/remoteSettings";
import type {
  AiAssistantCapability,
  CustomScreenSummary,
  PhoneWebcamCapability,
  ScreenViewCapability,
} from "../../foundation/protocol/messages";

export type ThemeMode = "system" | "light" | "dark";
export type SettingsSection =
  | "connection"
  | "custom-pointer"
  | "trackpad"
  | "keyboard"
  | "split"
  | "remote"
  | "appearance"
  | "app";
export type SettingsModeId = "trackpad" | "keyboard" | "remote" | FourthMode;

export interface SettingsToolOption {
  id: SettingsModeId;
  label: string;
  Icon: ComponentType<{ "aria-hidden"?: "true" }>;
}

export interface SettingsDrawerProps {
  activePc: PcProfile | null;
  appSettings: AppSettings;
  diagnostics: string;
  deviceName: string;
  customPointerEnabled?: boolean | undefined;
  accentColor?: string | null | undefined;
  accentColorOverridden?: boolean | undefined;
  accentColorSupported?: boolean | undefined;
  customScreens?: CustomScreenSummary[] | undefined;
  disconnectActivePc: () => void;
  forgetPc: (pcId: string) => void;
  installApp: () => Promise<void>;
  installPrompt: Event | null;
  isInstalled: boolean;
  isPairingQrReading?: boolean;
  isOpen: boolean;
  keyboardSettings: KeyboardSettings;
  onClose: () => void;
  onPairingQrSelected: (event: ChangeEvent<HTMLInputElement>) => Promise<void>;
  onManualHostSubmit: (target: ManualConnectionTarget) => void;
  onOpenGestureDebug?: (() => void) | undefined;
  onOpenGyroMouse?: (() => void) | undefined;
  onOpenMode?: (mode: SettingsModeId) => void;
  onOpenThirdPartyNotices: () => void;
  onOpenDiagnostics: () => void;
  onOpenAiAssistant?: (() => void) | undefined;
  onOpenCustomScreen?: ((screenId: string) => void) | undefined;
  onOpenScreenView?: (() => void) | undefined;
  onOpenPhoneWebcam?: (() => void) | undefined;
  screenViewCapability?: ScreenViewCapability | undefined;
  phoneWebcamCapability?: PhoneWebcamCapability | undefined;
  aiAssistantCapability?: AiAssistantCapability | undefined;
  pairedPcs: PcProfile[];
  pairingQrInputRef: RefObject<HTMLInputElement | null>;
  pairingScanMessage: string;
  presentationAvailable: boolean;
  filesAvailable?: boolean;
  terminalAvailable?: boolean;
  refreshInstalledApp: () => Promise<void>;
  refreshMessage: string;
  renameDevice: (name: string) => void;
  renamePc: (pcId: string, name: string) => void;
  remoteSettings: RemoteSettings;
  scanPairingQr: () => void;
  selectPc: (pcId: string) => void;
  setHostCustomPointer?: ((enabled: boolean) => void) | undefined;
  setHostAccentColor?: ((accentColor: string | null) => void) | undefined;
  setHostControlDepth?: ((controlDepth: boolean) => void) | undefined;
  setHostShowModeButtons?: ((showModeButtons: boolean) => void) | undefined;
  setThemeMode: Dispatch<SetStateAction<ThemeMode>>;
  showGestureDebug: boolean;
  supportsRemoteLaunch: boolean;
  themeMode: ThemeMode;
  controlDepth?: boolean | undefined;
  showModeButtons?: boolean | undefined;
  toolOptions: readonly SettingsToolOption[];
  trackpadSettings: TrackpadSettings;
  updateAppSetting: <Key extends keyof AppSettings>(key: Key, value: AppSettings[Key]) => void;
  updateKeyboardSetting: <Key extends keyof KeyboardSettings>(
    key: Key,
    value: KeyboardSettings[Key],
  ) => void;
  updateRemoteSetting: <Key extends keyof RemoteSettings>(
    key: Key,
    value: RemoteSettings[Key],
  ) => void;
  updateTrackpadSetting: <Key extends keyof TrackpadSettings>(
    key: Key,
    value: TrackpadSettings[Key],
  ) => void;
  usesLivePairingQr: boolean;
}
