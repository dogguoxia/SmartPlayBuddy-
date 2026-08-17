import type { WebSocket } from "ws";
import type { ClaimStatus, DeviceInfo, Message } from "./protocol.js";

export interface DeviceClient {
  id: string;
  socket: WebSocket;
  info: DeviceInfo;
  connectedAt: number;
  lastPingAt: number;
}

type EventListener = (event: { type: string; payload: unknown }) => void;

export class DeviceManager {
  private devices = new Map<string, DeviceClient>();
  private listeners = new Map<string, Set<EventListener>>();

  register(id: string, socket: WebSocket, status: ClaimStatus): DeviceClient {
    const device: DeviceClient = {
      id,
      socket,
      info: status.device,
      connectedAt: Date.now(),
      lastPingAt: Date.now(),
    };
    this.devices.set(id, device);
    this.emit(id, { type: "connected", payload: { device: this.toPublic(device) } });
    return device;
  }

  unregister(id: string): void {
    const device = this.devices.get(id);
    if (!device) return;
    this.devices.delete(id);
    this.emit(id, { type: "disconnected", payload: { id } });
    this.listeners.delete(id);
  }

  get(id: string): DeviceClient | undefined {
    return this.devices.get(id);
  }

  list() {
    return Array.from(this.devices.values()).map((d) => this.toPublic(d));
  }

  send(id: string, message: string): boolean {
    const device = this.devices.get(id);
    if (!device || device.socket.readyState !== 1) return false;
    device.socket.send(message);
    return true;
  }

  updatePing(id: string): void {
    const device = this.devices.get(id);
    if (device) device.lastPingAt = Date.now();
  }

  subscribe(id: string, listener: EventListener): () => void {
    if (!this.listeners.has(id)) {
      this.listeners.set(id, new Set());
    }
    const set = this.listeners.get(id)!;
    set.add(listener);
    return () => set.delete(listener);
  }

  emit(id: string, event: { type: string; payload: unknown }): void {
    const set = this.listeners.get(id);
    if (!set) return;
    for (const listener of set) {
      try {
        listener(event);
      } catch (err) {
        console.error("event listener error", err);
      }
    }
  }

  toPublic(device: DeviceClient) {
    return {
      id: device.id,
      info: device.info,
      connectedAt: device.connectedAt,
      lastPingAt: device.lastPingAt,
    };
  }
}
