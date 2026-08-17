export type MessageType = "session" | "system" | "command" | "response" | "error" | "stream";

export interface Message {
  type: MessageType;
  action: string;
  from?: string;
  to?: string;
  requestId?: string;
  timestamp: number;
  data?: unknown;
  binary?: boolean;
  binaryData?: Buffer;
}

export interface DeviceInfo {
  type: string;
  deviceName: string;
  deviceInfo: string;
  platform: string;
  machine: string;
  appVersion: string;
  screenResolution: string;
}

export interface ClaimStatus {
  device: DeviceInfo;
}

export function parseMessage(raw: string): Message {
  const parsed = JSON.parse(raw) as Record<string, unknown>;
  let data = parsed.data;

  if (typeof data === "string") {
    const decoded = Buffer.from(data, "base64");
    const text = decoded.toString("utf8");
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  return {
    type: String(parsed.type) as MessageType,
    action: String(parsed.action),
    from: parsed.from ? String(parsed.from) : undefined,
    to: parsed.to ? String(parsed.to) : undefined,
    requestId: parsed.requestId ? String(parsed.requestId) : undefined,
    timestamp: typeof parsed.timestamp === "number" ? parsed.timestamp : Date.now(),
    data,
    binary: parsed.binary === true,
  };
}

export function encodeData(data: unknown): string | undefined {
  if (data === undefined || data === null) return undefined;
  let raw: Buffer;
  if (Buffer.isBuffer(data)) {
    raw = data;
  } else if (typeof data === "string") {
    raw = Buffer.from(data, "utf8");
  } else {
    raw = Buffer.from(JSON.stringify(data), "utf8");
  }
  return raw.toString("base64");
}

export function serializeMessage(msg: Message): string {
  const payload: Record<string, unknown> = {
    type: msg.type,
    action: msg.action,
    timestamp: msg.timestamp,
  };
  if (msg.from !== undefined) payload.from = msg.from;
  if (msg.to !== undefined) payload.to = msg.to;
  if (msg.requestId !== undefined) payload.requestId = msg.requestId;
  if (msg.binary) payload.binary = true;

  const encoded = encodeData(msg.data);
  if (encoded !== undefined) payload.data = encoded;

  return JSON.stringify(payload);
}

export function createMessage(
  type: MessageType,
  action: string,
  opts: Partial<Omit<Message, "type" | "action">> = {}
): Message {
  return {
    type,
    action,
    timestamp: Date.now(),
    ...opts,
  };
}

export function createError(
  action: string,
  data: unknown,
  opts: Partial<Omit<Message, "type" | "action" | "data">> = {}
): Message {
  return createMessage("error", action, { ...opts, data });
}
