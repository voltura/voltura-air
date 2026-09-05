export const SOURCE = {
  sha: "95d1816a9f4f1cb4a33873c81c9d3987bad7b8f3",
  date: "5 September 2026",
  base: "https://github.com/voltura/voltura-air",
};
export const devices = {
  iphone: {
    label: "iPhone",
    family: "Phone",
    browser: "Safari",
    icon: "smartphone",
    note: "Use Safari. Add to Home Screen is optional; keep the controller visible for camera and motion features.",
  },
  android: {
    label: "Android phone",
    family: "Phone",
    browser: "Chrome",
    icon: "smartphone",
    note: "Use a current browser such as Chrome. Camera, motion and storage support depend on the device and browser.",
  },
  ipad: {
    label: "iPad",
    family: "Tablet",
    browser: "Safari",
    icon: "tablet",
    note: "Use Safari. Landscape gives you more working space; Files switches to two panels when at least 640 CSS pixels are available.",
  },
  tablet: {
    label: "Android tablet",
    family: "Tablet",
    browser: "Chrome",
    icon: "tablet",
    note: "Use a current browser such as Chrome. Landscape can show the keyboard and trackpad side by side.",
  },
  computer: {
    label: "Another computer",
    family: "Computer",
    browser: "Modern browser",
    icon: "monitor",
    note: "The controller can run on Windows, macOS or Linux. The PC you control must run Windows 11. A physical mouse and keyboard enable direct screen control.",
  },
};
export const modes = {
  local: {
    label: "Standard Local",
    short: "Local",
    icon: "wifi",
    subtitle: "Same network · no cloud",
    description:
      "The Windows PC serves the controller. Your devices connect on the same trusted local network; no internet or Voltura service is needed.",
    route: "All traffic stays on your local network",
    setup:
      "On the PC, open Connection → Direct local network. Leave Enable enhanced device features off, then Save and restart if you changed a setting. Open Connect.",
    note: "HTTPS-only browser features are unavailable through the normal local HTTP link.",
  },
  enhanced: {
    label: "Enhanced Direct",
    short: "Enhanced Direct",
    icon: "shield-check",
    subtitle: "Same network · HTTPS features",
    description:
      "The secure controller loads from voltura.se. Internet is needed to load it and finish connection setup; established control and media stay on the private LAN.",
    route: "Internet for setup. Direct on your LAN after that.",
    setup:
      "On the PC, open Connection → Direct local network. Select Enable enhanced device features, then Save and restart. Open Connect and use the primary QR or Copy link.",
    note: "Both devices still need the same reachable private LAN. This does not give you remote access over the internet.",
  },
  relay: {
    label: "Cloud Relay",
    short: "Relay",
    icon: "cloud",
    subtitle: "Different or restricted networks",
    description:
      "Both devices connect outward through Voltura. Use it across networks or where inbound PC connections are blocked. Internet is required throughout.",
    route: "Encrypted connection through Voltura’s relay",
    setup:
      "On the PC, open Connection → Cloud relay through Voltura, then Save and restart. Open Connect and use the new QR or Copy link.",
    note: "Relay does not automatically fall back to Direct. Media shares a service-wide monthly allowance; this is not a personal data allocation.",
  },
};
export const goals = [
  { id: "all", label: "Explore everything", icon: "grid-2x2" },
  { id: "control", label: "Control my PC", icon: "mouse-pointer-2" },
  { id: "screen", label: "View & capture", icon: "monitor-play" },
  { id: "files", label: "Manage files", icon: "folder-open" },
  { id: "camera", label: "Use my camera", icon: "camera" },
  { id: "present", label: "Present & play", icon: "presentation" },
  { id: "work", label: "Work & type", icon: "keyboard" },
  { id: "system", label: "PC tools", icon: "settings-2" },
];
export const permissions = {
  input: "Pointer and keyboard",
  screen: "View PC screen",
  webcam: "Phone webcam",
  browse: "Browse and open files",
  change: "Change files",
  transfer: "Transfer files",
  apps: "Control open applications",
  launch: "Application launch",
  presentation: "Presentation",
  clipboard: "Read PC clipboard",
  terminal: "Terminal",
  volume: "Volume",
  lock: "Lock",
  blackout: "Blackout",
  sleep: "Sleep",
  restart: "Restart",
  shutdown: "Shutdown",
  awake: "Keep awake",
  diagnostics: "View diagnostics",
};
const remote = new Set(["input", "apps", "launch", "presentation", "volume", "lock", "blackout"]);
export const features = [
  {
    id: "screen",
    title: "View PC screen",
    icon: "monitor-play",
    goals: ["screen", "control"],
    summary: "See a Windows display live, with touch control and optional PC sound.",
    permissions: ["screen"],
    optional: { input: "To move, click, scroll and type" },
    capabilities: [
      "Choose one connected Windows display; switch while viewing.",
      "On touch devices, move the pointer with one finger. Pinch and pan locally up to 10×, or switch two fingers to PC scrolling.",
      "Turn on Sound to hear PC system audio. Every new connection starts muted.",
    ],
    limits: [
      "One viewing device and one display at a time. The Windows PC must be awake and signed in.",
      "Protected content and the Windows secure desktop are not captured. This is not a gaming or multi-monitor streaming tool.",
    ],
    steps: [
      "On the controller, open Menu → View PC screen and choose a display.",
      "Use Sound to unmute. Use Zoom / Scroll to choose what two-finger gestures do.",
    ],
    source: "live-screen-mirror",
    media: true,
  },
  {
    id: "trackpad",
    title: "Trackpad & gestures",
    icon: "mouse-pointer-2",
    goals: ["control"],
    summary: "Move, click, drag and scroll from your phone or tablet.",
    permissions: ["input"],
    capabilities: [
      "Use one finger to move; tap to click, long press or use two fingers to right-click.",
      "Hold a mouse button to drag. Use two-axis scrolling and optional pinch zoom.",
      "Adjust pointer speed, handedness and larger buttons in Settings.",
    ],
    limits: [
      "Commands act on the currently signed-in Windows desktop. The PC cannot be woken remotely.",
    ],
    steps: [
      "Open Trackpad on the controller.",
      "Use the mouse buttons beneath the touch surface; adjust the controls in Menu → Settings.",
    ],
    source: "trackpad",
  },
  {
    id: "gyro",
    title: "Gyro mouse",
    icon: "move-3d",
    goals: ["control", "present"],
    summary: "Hold the surface and aim your device to steer the pointer.",
    permissions: ["input"],
    https: true,
    mobile: true,
    browser:
      "Motion sensors and browser motion access are required. Device type alone cannot guarantee support.",
    capabilities: [
      "Hold the Trackpad surface or a mouse button while moving the device. Release to stop.",
      "Tap and double-tap to click; a two-finger drag still scrolls.",
      "Also works on Presentation’s embedded trackpad and configured Custom screen trackpads.",
    ],
    limits: [
      "Motion permission must be granted when prompted. It turns off when the page is hidden or the connection changes.",
      "Gyro is not available inside the Screen View trackpad.",
    ],
    steps: [
      "Open Trackpad → Gyro.",
      "Allow motion access if asked, then hold the surface and move the device.",
    ],
    source: "trackpad",
  },
  {
    id: "desktop",
    title: "Control from another computer",
    icon: "monitor",
    goals: ["control", "work", "screen"],
    summary: "Use your physical mouse and keyboard directly on the live PC picture.",
    permissions: ["screen", "input"],
    computer: true,
    capabilities: [
      "Map mouse movement, left/right click, dragging and scrolling directly to the selected display.",
      "Send physical typing and supported shortcuts while direct control is active.",
    ],
    limits: [
      "Needs a fine hovering pointer (mouse or trackpad) and a physical keyboard. Some browser-reserved shortcuts remain local.",
      "Control starts off and resets after reconnecting, changing displays or leaving the view.",
    ],
    steps: [
      "Open Menu → View PC screen and choose a display.",
      "Activate the mouse-and-keyboard icon beside Scroll / Zoom. Click and type over the PC picture.",
    ],
    source: "live-screen-mirror",
    media: true,
  },
  {
    id: "screenshot",
    title: "Save a screenshot",
    icon: "scan-line",
    goals: ["screen"],
    summary: "Save a clean, full-resolution PNG of the display you are watching.",
    permissions: ["screen", "transfer"],
    https: true,
    browser: "The browser must support writable private storage (OPFS) for staging the image.",
    capabilities: [
      "Capture the display at its native resolution, without the cursor or controller controls.",
      "Use the existing Save / Share flow on your device; the live view keeps running.",
    ],
    limits: [
      "No PC-side temporary image or screenshot history. Save the image before leaving the workspace.",
      "A screenshot and a screen recording cannot be staged at the same time.",
    ],
    steps: [
      "Start View PC screen, then tap its camera icon.",
      "When the image is ready, choose Save to Files / Share or the browser download option.",
    ],
    source: "live-screen-mirror",
    media: true,
  },
  {
    id: "recording",
    title: "Record the PC screen",
    icon: "circle-dot",
    goals: ["screen"],
    summary: "Keep up to five minutes of the incoming picture on your device.",
    permissions: ["screen"],
    https: true,
    browser:
      "Requires MediaRecorder, writable private storage (OPFS), and at least 512 MiB of confirmed available browser storage.",
    capabilities: [
      "Record the clean incoming video, without cursor overlays, local zoom or controller controls.",
      "Include PC sound by turning Sound on before you begin.",
      "Save or share the finished recording as browser-supported MP4 or WebM.",
    ],
    limits: [
      "Maximum five minutes. Recording stops near 480 MiB and never exceeds 512 MiB. No pause, editing or recording history.",
      "Sound is fixed during a recording. Leaving the workspace or page discards the staged file; save first.",
      "Transfer files permission is not required for device-local recording.",
    ],
    steps: [
      "Start View PC screen and decide whether Sound should be on.",
      "Choose Record, then Stop when done. Save / Share before leaving the workspace.",
    ],
    source: "live-screen-mirror",
    media: true,
  },
  {
    id: "files",
    title: "Browse & open PC files",
    icon: "folder-open",
    goals: ["files", "work"],
    summary: "Explore PC folders and mapped drives without moving files off the PC.",
    permissions: ["browse"],
    optional: { screen: "To open a file and continue into View PC screen" },
    capabilities: [
      "Browse, sort, select and inspect properties for files on the PC or mapped drives.",
      "Use one panel on narrow views and two panels at 640 CSS pixels or wider.",
      "Open launches the file on the PC. View opens it and then shows the PC screen when permitted.",
    ],
    limits: [
      "The Windows account must have access to the location. Protected operating-system items are hidden by default.",
      "This does not browse folders on your phone and is not file synchronization.",
    ],
    steps: [
      "Open Menu → Files. Choose a drive or a known folder.",
      "Select a file, then Open to launch it on the PC, or View to continue into screen viewing.",
    ],
    source: "files",
  },
  {
    id: "change",
    title: "Organize PC files",
    icon: "folder-cog",
    goals: ["files"],
    summary: "Copy, move, rename or recycle files, with progress and conflict choices.",
    permissions: ["browse", "change"],
    capabilities: [
      "Use Copy / Move between two panels, or Windows clipboard Cut / Copy / Paste.",
      "Review progress, conflicts and pending operations in the Operation Center.",
      "Delete asks for confirmation and uses the Recycle Bin.",
    ],
    limits: [
      "Windows account access still applies. Items that cannot be recycled are rejected.",
      "A host restart does not automatically resume interrupted operations.",
    ],
    steps: [
      "Open Files and select the items. Set the destination in the other panel when using two panels.",
      "Choose the action and review any confirmation or conflict choices.",
    ],
    source: "files",
  },
  {
    id: "download",
    title: "Save a PC file to your device",
    icon: "download",
    goals: ["files"],
    summary: "Download one selected file, then save it or hand it to another app.",
    permissions: ["browse", "transfer"],
    https: true,
    browser:
      "Saving to the device needs writable private browser storage (OPFS) and enough free space.",
    capabilities: [
      "Transfer one explicitly selected PC file with progress and cancellation.",
      "Save to Files / Share after the transfer, with browser download fallback.",
    ],
    limits: [
      "One transfer runs across the host at a time. No batches, pause/resume, synchronization or transfer history.",
      "A retry starts from zero. Save before navigating away or disconnecting.",
    ],
    steps: [
      "In Files, select one file → Transfer → Save to this device.",
      "After transfer completes, choose Save to Files / Share.",
    ],
    source: "files",
    media: true,
  },
  {
    id: "upload",
    title: "Send a file to the PC",
    icon: "upload",
    goals: ["files", "work"],
    summary: "Choose a file on this device and put it in the active PC folder.",
    permissions: ["browse", "change", "transfer"],
    capabilities: [
      "Upload a chosen device file into the current Files folder.",
      "Handle name conflicts with Replace, Keep both or Cancel.",
    ],
    limits: [
      "One file at a time; no batch, synchronization, pause or resume. A retry starts from zero.",
      "The PC folder must be writable and have enough space. Uploading does not require device-side OPFS storage.",
    ],
    steps: [
      "Open Files and navigate to the destination folder on the PC.",
      "Choose Transfer → Choose file from this device and select a file.",
    ],
    source: "files",
    media: true,
  },
  {
    id: "photo",
    title: "Take a photo → save to PC",
    icon: "image-plus",
    goals: ["files", "camera"],
    summary: "Capture a photo and upload the original image into your PC folder.",
    permissions: ["browse", "change", "transfer"],
    capabilities: [
      "Use the browser’s native photo capture or image picker.",
      "Save the original image directly into the active PC folder.",
    ],
    limits: [
      "The browser may show an image picker instead of opening a camera, especially on a computer.",
      "One image at a time. No image processing or synchronization. This native picker flow also works without Enhanced Direct.",
    ],
    steps: [
      "Open Files and choose the destination folder.",
      "Choose Transfer → Take photo, take or select an image, then confirm the upload.",
    ],
    source: "files",
    media: true,
  },
  {
    id: "webcam",
    title: "Use phone as webcam",
    icon: "camera",
    goals: ["camera"],
    summary: "Make your device’s camera available as Voltura Air Webcam in Windows apps.",
    permissions: ["webcam"],
    https: true,
    browser:
      "A camera and browser camera permission are required. Supported capture depends on the device.",
    extra:
      "Install the optional Phone Webcam component on Windows. Installed apps → Voltura Air → Modify lets you add or repair it and may ask for administrator approval.",
    capabilities: [
      "Choose an available camera before starting, and switch while streaming.",
      "Select Voltura Air Webcam as the camera in a Windows app.",
      "Optional phone microphone audio is a separate setup; it starts off.",
    ],
    limits: [
      "Keep the controller page visible: hiding it immediately releases the camera.",
      "One selected camera session. Video requests up to 1080p at 30 fps; actual quality depends on the camera and connection.",
    ],
    steps: [
      "On the PC, check Phone webcam reports that the component is ready.",
      "On the controller, open Menu → Phone webcam. Allow camera access, select a camera and Start webcam.",
      "In the Windows app, select Voltura Air Webcam as the camera.",
    ],
    source: "phone-webcam",
    media: true,
  },
  {
    id: "microphone",
    title: "Add webcam microphone audio",
    icon: "mic",
    goals: ["camera"],
    summary: "Send device microphone audio alongside your webcam video.",
    permissions: ["webcam"],
    https: true,
    browser: "Requires camera and microphone access in the browser.",
    extra:
      "Set up Phone Webcam first. Install VB-CABLE separately from VB-Audio on the PC; it is third-party donationware and is not included with Voltura Air.",
    capabilities: [
      "Enable Use microphone before starting the webcam; use Mute during the session.",
      "Select CABLE Output as the microphone in the Windows receiving app.",
    ],
    limits: [
      "Use microphone appears only when the required VB-CABLE endpoint is ready. Audio starts off.",
      "For the PC’s Test audio option, use headphones or keep the phone away from the speakers to avoid feedback.",
    ],
    steps: [
      "Install the Phone Webcam component and VB-CABLE on the PC.",
      "In the controller’s Phone webcam tool, enable Use microphone and allow access. Start webcam.",
      "Select Voltura Air Webcam for video and CABLE Output for microphone input in the Windows app.",
    ],
    source: "phone-webcam",
    media: true,
  },
  {
    id: "keyboard",
    title: "Keyboard & split view",
    icon: "keyboard",
    goals: ["control", "work"],
    summary: "Type, use shortcuts, or put a keyboard beside your trackpad.",
    permissions: ["input"],
    capabilities: [
      "Use live typing or send buffered multiline text, plus navigation keys, F1–F12 and common shortcuts.",
      "A wide landscape layout can place the keyboard and trackpad side by side.",
    ],
    limits: [
      "Typing goes to the focused PC application. Available split layout depends on space, not just the device name.",
    ],
    steps: [
      "Open Keyboard and focus the target application on the PC.",
      "For more workspace, rotate a tablet to landscape and use the split layout controls.",
    ],
    source: "keyboard",
  },
  {
    id: "text",
    title: "Dictate & send text",
    icon: "text-cursor-input",
    goals: ["work"],
    summary: "Send editable text or dictated words to a PC app, document or draft.",
    permissions: ["input"],
    browser:
      "Browser speech recognition varies. If unavailable, use the device keyboard’s dictation in the text field.",
    capabilities: [
      "Send text to the focused app, PC clipboard or configured document/email destinations.",
      "Keep up to 20 browser-local snippets and reuse them without sending automatically.",
    ],
    limits: [
      "Up to 4,096 characters per text. Sending 2,000 or more asks for confirmation.",
      "Speech recognition support is browser-dependent; this explorer does not test the selected device’s microphone.",
    ],
    steps: [
      "Open Menu → Dictation or Send text.",
      "Type or dictate, review the text and destination, then send it.",
    ],
    source: "dictation-and-text-transfer",
  },
  {
    id: "clipboard",
    title: "Get PC clipboard text",
    icon: "clipboard",
    goals: ["work"],
    summary: "Bring PC clipboard text into a visible box on your device.",
    permissions: ["clipboard"],
    optional: { input: "To send edited text back to the PC" },
    capabilities: [
      "Get PC clipboard text into an editable box after an explicit request.",
      "On a supporting HTTPS controller, copy fresh PC text directly into the device clipboard.",
    ],
    limits: [
      "Plain text only, up to 4,096 characters. Clipboards are never watched or automatically synchronized.",
      "Direct device clipboard access needs HTTPS and browser support. The visible-box action remains useful on Standard Local.",
    ],
    steps: [
      "Open Menu → Get text. Choose Get PC clipboard text into this box.",
      "On HTTPS, use the direct device-clipboard option if the browser supports it.",
    ],
    source: "dictation-and-text-transfer",
  },
  {
    id: "apps",
    title: "Switch & close PC apps",
    icon: "panels-top-left",
    goals: ["control", "work"],
    summary: "Flick through open windows, bring one forward or close it normally.",
    permissions: ["apps"],
    optional: {
      screen: "For static window previews",
      launch: "For host-approved Open app shortcuts",
    },
    capabilities: [
      "Flick the circular card deck and tap the centered window to restore or focus it.",
      "Swipe up or use Close to request normal app closure.",
      "Use Open app for shortcuts configured on the PC when Application launch is allowed.",
    ],
    limits: [
      "Only ordinary windows in the signed-in session and current virtual desktop. Unsaved-work prompts remain on the PC.",
      "Previews are not live and may be unavailable for minimized or uncapturable windows.",
      "Controlling the Voltura Air host window has a separate PC setting, disabled by default; Remote controls never gets this access.",
    ],
    steps: [
      "Open Menu → Apps.",
      "Center a window and tap it to focus, or use Close. Use Open app for approved shortcuts.",
    ],
    source: "apps",
  },
  {
    id: "presentation",
    title: "Presentation controls",
    icon: "presentation",
    goals: ["present"],
    summary: "Advance slides, use the laser pointer, and track your presentation time.",
    permissions: ["presentation"],
    optional: { input: "To use the embedded trackpad" },
    capabilities: [
      "Control PowerPoint or use supported Google Slides and PDF/browser presentation controls.",
      "Use a laser pointer, timing and PC-saved presentation reports.",
      "Choose an available PowerPoint presentation and explicitly start it.",
    ],
    limits: [
      "The deck or presentation application lives on the PC. Available controls depend on the presentation type.",
      "Gyro steering needs HTTPS, sensors and Pointer and keyboard permission separately.",
    ],
    steps: [
      "Open the presentation on the PC.",
      "Open Presentation on the controller, choose the presentation when asked and start it.",
    ],
    source: "presentation",
  },
  {
    id: "media",
    title: "Media & volume remote",
    icon: "play",
    goals: ["present", "control"],
    summary: "Control playback, PC volume, browser navigation and approved apps.",
    permissions: ["input", "volume"],
    optional: { launch: "To launch applications configured on the PC" },
    capabilities: [
      "Use media keys, volume and mute, plus browser and window shortcuts.",
      "Use the Kodi remote or configured app shortcuts from Remote.",
    ],
    limits: [
      "Playback commands depend on the focused application and its support for the relevant keys. App launch buttons must be configured on the PC.",
    ],
    steps: [
      "Open the target media app on the PC.",
      "Choose Remote on the controller and use its media, navigation and volume controls.",
    ],
    source: "remote",
  },
  {
    id: "custom",
    title: "Custom control screens",
    icon: "layout-dashboard",
    goals: ["control", "present"],
    summary: "Use a personal mix of buttons, shortcuts, trackpads and navigation controls.",
    permissions: [],
    customActions: true,
    capabilities: [
      "Design and assign responsive screens in the PC’s Custom screens editor.",
      "Combine shortcuts, approved apps, panels, trackpads, navigation rings and D-pads.",
      "Import or export .volturascreen packages and browse the community screen library.",
    ],
    limits: [
      "Each control keeps its own underlying permission. A Custom screen does not grant extra access.",
      "Screens are designed and assigned on the PC, then used from the paired device.",
    ],
    steps: [
      "On the PC, open Custom screens. Create or import a screen, preview it, and assign it.",
      "Open the assigned screen from the controller’s selector.",
    ],
    source: "custom-screens",
  },
  {
    id: "terminal",
    title: "Windows Terminal session",
    icon: "terminal",
    goals: ["system", "work"],
    summary: "Open a PowerShell session on your PC from the controller.",
    permissions: ["terminal"],
    capabilities: [
      "Type commands, select output, copy and scroll with touch shortcuts.",
      "Keep the session running while navigating within the controller.",
    ],
    limits: [
      "Runs as the signed-in Windows user and can read or change anything that account can access. It is not a sandbox. UAC prompts stay on the physical PC.",
      "One session across the host. Unexpected disconnects retain it for up to 15 minutes for the same device; reload does not preserve terminal contents.",
    ],
    steps: [
      "Allow Terminal only for a trusted personal device. A current PC identity pairing is required; scan a fresh QR if asked.",
      "Open Menu → Terminal and start the session.",
    ],
    source: "terminal",
    media: true,
  },
  {
    id: "assistant",
    title: "AI Assistant",
    icon: "sparkles",
    goals: ["system", "work"],
    summary: "Ask for Voltura Air help and find likely local document filenames.",
    permissions: [],
    myOnly: true,
    extra:
      "Codex must be installed and signed in on the PC, with its command-line component available. Check AI Assistant on the Windows host and use Retry after setup.",
    capabilities: [
      "Ask questions about Voltura Air and troubleshoot using bundled project documentation.",
      "Find likely document filenames under the Windows user profile; results include paths and metadata, not file contents.",
    ],
    limits: [
      "Only the exact My device profile can open the paired-device tool. Custom is not sufficient even with every permission allowed.",
      "Read-only tools cannot execute commands, change files/settings, control apps or search the web. The conversation is stored by Codex on the PC.",
      "Use only on a trusted device: filenames and paths can still contain private information. Codex uses the PC’s existing account and connection.",
    ],
    steps: [
      "On the PC, open AI Assistant and complete any Codex setup guidance.",
      "With the My device profile, open Menu → AI Assistant on the controller when it becomes available.",
    ],
    source: "ai-assistant",
  },
  {
    id: "power",
    title: "Lock, blackout & power",
    icon: "power",
    goals: ["system", "control"],
    permissions: ["lock", "blackout"],
    optional: { sleep: "For Sleep", restart: "For Restart", shutdown: "For Shut down" },
    summary: "Lock the PC, cover its displays or use permitted power actions.",
    capabilities: [
      "Lock the PC or use Blackout to cover connected monitors without changing power state.",
      "Other separately permitted actions include sleep, display off, screen saver, sign out, restart and shutdown.",
    ],
    limits: [
      "The controller cannot wake a sleeping or shut-down PC. Display off can suspend some PCs and require physical input to wake them.",
      "Blackout ends on local or remote input. Power actions are separately permission-controlled.",
    ],
    steps: [
      "Open Remote and its power controls.",
      "Choose the intended action and read any confirmation before proceeding.",
    ],
    source: "input-and-windows-actions",
  },
  {
    id: "awake",
    title: "Keep the PC awake",
    icon: "sun",
    goals: ["system"],
    summary: "Prevent sleep for a duration, until a chosen time, or indefinitely.",
    permissions: ["awake"],
    capabilities: [
      "Choose a timed, date/time or indefinite Keep awake mode.",
      "Optionally keep the screen on without changing the Windows power plan.",
    ],
    limits: [
      "The PC must already be running. Keep awake does not power on or wake the computer.",
      "The separate Simulate activity option is host-only. It does not move or click the pointer.",
    ],
    steps: [
      "While connected, open the Keep awake controls and choose a duration.",
      "Use Off when the PC should return to normal power behavior.",
    ],
    source: "input-and-windows-actions",
  },
  {
    id: "diagnostics",
    title: "Connection diagnostics",
    icon: "activity",
    goals: ["system"],
    summary: "Inspect connection and device details to help troubleshoot your setup.",
    permissions: ["diagnostics"],
    capabilities: [
      "View available device and connection diagnostics and copy relevant details.",
      "Use the Windows Diagnostics page for host information and optional logs.",
    ],
    limits: [
      "View diagnostics is a separate device permission. Available fields depend on the running host.",
      "Optional usage statistics and application logging are controlled separately on the PC.",
    ],
    steps: [
      "On the controller, open Menu → Diagnostics if permitted.",
      "If the connection cannot start, use the PC’s Connect, Connection and Diagnostics pages.",
    ],
    source: "diagnostics",
  },
];
export function evaluate(feature, state) {
  const blockers = [],
    checks = [];
  if (feature.mobile && state.device === "computer")
    blockers.push({
      code: "device",
      text: "Use a sensor-equipped phone or tablet for this feature.",
    });
  if (feature.computer && state.device !== "computer")
    checks.push(
      "Direct control needs a fine hovering pointer and physical keyboard. A touch-only device uses the regular Screen View touch controls.",
    );
  if (feature.https && state.mode === "local")
    blockers.push({
      code: "https",
      text: "The normal Standard Local HTTP link cannot provide the secure browser APIs this feature needs. Choose Enhanced Direct on your LAN or Cloud Relay across networks.",
    });
  if (feature.myOnly && state.profile !== "my")
    blockers.push({
      code: "profile",
      text: "AI Assistant requires exactly the My device profile. Remote controls and Custom cannot open it.",
    });
  const denied =
    state.profile === "remote" ? feature.permissions.filter((p) => !remote.has(p)) : [];
  if (denied.length)
    blockers.push({
      code: "permission",
      text:
        "Remote controls blocks: " +
        denied.map((p) => permissions[p]).join(", ") +
        ". Change this device’s access on the PC.",
    });
  if (state.profile === "custom" && feature.permissions.length)
    checks.push(
      "Check each required permission in Devices on the PC; Custom permissions cannot be inferred here.",
    );
  if (feature.browser) checks.push(feature.browser);
  if (feature.extra) checks.push(feature.extra);
  if (feature.customActions)
    checks.push("Availability depends on the permissions for the controls in the assigned screen.");
  return {
    status: blockers.length ? "blocked" : checks.length ? "conditional" : "available",
    label: blockers.length
      ? blockers[0].code === "https"
        ? "Needs HTTPS"
        : blockers[0].code === "device"
          ? "Use a mobile device"
          : "Needs permission"
      : checks.length
        ? "Check requirements"
        : "Available",
    blockers,
    checks,
  };
}
export function permissionState(key, profile) {
  return profile === "custom"
    ? "Check on PC"
    : profile === "my" || remote.has(key)
      ? "Allowed"
      : "Blocked";
}
export function getFeatures(state, query = "") {
  const q = query.trim().toLocaleLowerCase();
  return features.filter(
    (f) =>
      (state.goal === "all" || f.goals.includes(state.goal)) &&
      (!q ||
        [
          f.title,
          f.summary,
          ...f.capabilities,
          ...f.limits,
          ...f.permissions.map((p) => permissions[p]),
        ]
          .join(" ")
          .toLocaleLowerCase()
          .includes(q)),
  );
}
export function cleanState(input = {}) {
  return {
    device: Object.hasOwn(devices, input.device) ? input.device : "iphone",
    mode: Object.hasOwn(modes, input.mode) ? input.mode : "enhanced",
    goal: goals.some((g) => g.id === input.goal) ? input.goal : "all",
    profile: ["my", "remote", "custom"].includes(input.profile) ? input.profile : "my",
  };
}
export function setupSteps(feature, state) {
  return [
    {
      title: "Get the Windows PC ready",
      text: "Install Voltura Air on Windows 11 and keep the host running, awake and signed in.",
    },
    { title: "Choose " + modes[state.mode].label, text: modes[state.mode].setup },
    {
      title: "Pair your " + devices[state.device].label.toLowerCase(),
      text:
        state.device === "computer"
          ? "Use Copy link on the PC’s Connect page, open that exact link in the other computer’s browser, then confirm the device name. Never share a pairing link publicly."
          : "Scan the QR code on the PC’s Connect page with your device, open the browser link, and confirm the device name. Use a new code if the old one expired.",
    },
    {
      title: "Review access on the PC",
      text: feature.myOnly
        ? "Open Devices → this device and select My device."
        : feature.permissions.length
          ? "Open Devices → this device and allow " +
            feature.permissions.map((p) => permissions[p]).join(", ") +
            "."
          : "Open Devices → this device and review the permissions for the actions you intend to use.",
    },
    ...feature.steps.map((text, i) => ({
      title: i === 0 ? "Open the feature" : "Continue on your device",
      text,
    })),
  ];
}
