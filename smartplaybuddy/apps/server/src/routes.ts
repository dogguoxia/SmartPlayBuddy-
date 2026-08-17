import type { IncomingMessage, ServerResponse } from "http";
import type { DeviceManager } from "./device-manager.js";
import { createMessage, serializeMessage } from "./protocol.js";
import { listScreenshots, readScreenshot } from "./screenshot-store.js";

function setCors(res: ServerResponse): void {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");
}

function sendJson(res: ServerResponse, status: number, data: unknown): void {
  setCors(res);
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(data));
}

async function readBody(req: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8");
}

export function createRouter(deviceManager: DeviceManager) {
  return async (req: IncomingMessage, res: ServerResponse): Promise<boolean> => {
    const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);
    const pathname = url.pathname;

    if (req.method === "OPTIONS") {
      setCors(res);
      res.writeHead(204);
      res.end();
      return true;
    }

    if (pathname === "/api/health") {
      sendJson(res, 200, { status: "ok", devices: deviceManager.list().length });
      return true;
    }

    if (pathname === "/api/devices") {
      sendJson(res, 200, { devices: deviceManager.list() });
      return true;
    }

    if (pathname === "/api/screenshots") {
      const deviceId = url.searchParams.get("deviceId") ?? undefined;
      sendJson(res, 200, { screenshots: listScreenshots(deviceId) });
      return true;
    }

    const screenshotMatch = pathname.match(/^\/api\/screenshots\/([^/]+)$/);
    if (screenshotMatch && req.method === "GET") {
      const id = decodeURIComponent(screenshotMatch[1] as string);
      const data = readScreenshot(id);
      if (!data) {
        sendJson(res, 404, { error: "screenshot not found" });
        return true;
      }
      const ext = id.split(".").pop()?.toLowerCase() ?? "png";
      const mime = ext === "jpg" || ext === "jpeg" ? "image/jpeg" : `image/${ext}`;
      setCors(res);
      res.writeHead(200, { "Content-Type": mime });
      res.end(data);
      return true;
    }

    const commandMatch = pathname.match(/^\/api\/devices\/([^/]+)\/command$/);
    if (commandMatch && req.method === "POST") {
      const id = decodeURIComponent(commandMatch[1] as string);
      const device = deviceManager.get(id);
      if (!device) {
        sendJson(res, 404, { error: "device not found" });
        return true;
      }

      const body = await readBody(req);
      let payload: { action?: string; data?: unknown } = {};
      try {
        payload = JSON.parse(body);
      } catch {
        sendJson(res, 400, { error: "invalid json body" });
        return true;
      }

      if (!payload.action) {
        sendJson(res, 400, { error: "action is required" });
        return true;
      }

      const msg = createMessage("command", payload.action, {
        to: id,
        data: payload.data ?? {},
      });
      const ok = deviceManager.send(id, serializeMessage(msg));
      if (!ok) {
        sendJson(res, 502, { error: "failed to send command to device" });
        return true;
      }
      sendJson(res, 202, { accepted: true, requestId: msg.requestId });
      return true;
    }

    const eventsMatch = pathname.match(/^\/api\/devices\/([^/]+)\/events$/);
    if (eventsMatch && req.method === "GET") {
      const id = decodeURIComponent(eventsMatch[1] as string);
      setCors(res);
      res.writeHead(200, {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        "Connection": "keep-alive",
      });

      const sendEvent = (event: { type: string; payload: unknown }) => {
        res.write(`event: ${event.type}\n`);
        res.write(`data: ${JSON.stringify(event.payload)}\n\n`);
      };

      sendEvent({ type: "subscribed", payload: { id } });
      const unsubscribe = deviceManager.subscribe(id, sendEvent);

      const keepAlive = setInterval(() => {
        res.write(":keepalive\n\n");
      }, 15000);

      req.on("close", () => {
        clearInterval(keepAlive);
        unsubscribe();
      });

      return true;
    }

    return false;
  };
}
