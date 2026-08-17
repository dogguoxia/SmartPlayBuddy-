const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:2508";

export interface DeviceInfo {
  type: string;
  deviceName: string;
  deviceInfo: string;
  platform: string;
  machine: string;
  appVersion: string;
  screenResolution: string;
}

export interface Device {
  id: string;
  info: DeviceInfo;
  connectedAt: number;
  lastPingAt: number;
}

export async function getDevices(): Promise<Device[]> {
  const res = await fetch(`${API_BASE}/api/devices`, { cache: "no-store" });
  if (!res.ok) throw new Error("failed to fetch devices");
  const json = (await res.json()) as { devices: Device[] };
  return json.devices;
}

export async function sendCommand(
  deviceId: string,
  action: string,
  data?: unknown
): Promise<{ accepted: boolean; requestId?: string }> {
  const res = await fetch(`${API_BASE}/api/devices/${encodeURIComponent(deviceId)}/command`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ action, data }),
  });
  if (!res.ok) throw new Error("failed to send command");
  return (await res.json()) as { accepted: boolean; requestId?: string };
}

export async function sendChatMessage(
  deviceId: string,
  text: string
): Promise<{ accepted: boolean; requestId?: string }> {
  const res = await fetch(`${API_BASE}/api/devices/${encodeURIComponent(deviceId)}/command`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ action: "chat", data: text }),
  });
  if (!res.ok) throw new Error("failed to send chat message");
  return (await res.json()) as { accepted: boolean; requestId?: string };
}

export interface Screenshot {
  id: string;
  deviceId: string;
  action: string;
  filename: string;
  size: number;
  createdAt: number;
}

export async function getScreenshots(deviceId?: string): Promise<Screenshot[]> {
  const url = deviceId
    ? `${API_BASE}/api/screenshots?deviceId=${encodeURIComponent(deviceId)}`
    : `${API_BASE}/api/screenshots`;
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) throw new Error("failed to fetch screenshots");
  const json = (await res.json()) as { screenshots: Screenshot[] };
  return json.screenshots;
}

export interface EventHandlers {
  onConnected?: (payload: unknown) => void;
  onResponse?: (payload: unknown) => void;
  onStream?: (payload: unknown) => void;
  onError?: (payload: unknown) => void;
  onDisconnected?: (payload: unknown) => void;
  onBinary?: (payload: unknown) => void;
  onChat?: (payload: unknown) => void;
}

export function subscribeEvents(deviceId: string, handlers: EventHandlers): () => void {
  const es = new EventSource(
    `${API_BASE}/api/devices/${encodeURIComponent(deviceId)}/events`
  );

  const wrap = (key: keyof EventHandlers) => (e: MessageEvent) => {
    const fn = handlers[key];
    if (!fn) return;
    try {
      fn(JSON.parse(e.data));
    } catch {
      fn(e.data);
    }
  };

  es.addEventListener("connected", wrap("onConnected"));
  es.addEventListener("response", wrap("onResponse"));
  es.addEventListener("stream", wrap("onStream"));
  es.addEventListener("error", wrap("onError"));
  es.addEventListener("disconnected", wrap("onDisconnected"));
  es.addEventListener("binary", wrap("onBinary"));
  es.addEventListener("chat", wrap("onChat"));

  return () => es.close();
}
