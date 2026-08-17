import { createServer as createHttpServer } from "http";
import { WebSocketServer, type WebSocket } from "ws";
import { DeviceManager } from "./device-manager.js";
import { createRouter } from "./routes.js";
import { createMessage, parseMessage, serializeMessage } from "./protocol.js";
import { saveScreenshot } from "./screenshot-store.js";

export function createServer(port: number) {
  const deviceManager = new DeviceManager();
  const router = createRouter(deviceManager);

  const httpServer = createHttpServer(async (req, res) => {
    const handled = await router(req, res);
    if (!handled) {
      res.writeHead(404, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "not found" }));
    }
  });

  const wss = new WebSocketServer({ server: httpServer, path: "/ws" });

  wss.on("connection", (socket: WebSocket) => {
    let deviceId: string | null = null;
    let pendingBinaryMessage: ReturnType<typeof parseMessage> | null = null;

    const cleanup = () => {
      if (deviceId) {
        deviceManager.unregister(deviceId);
      }
    };

    socket.on("message", (raw: Buffer | ArrayBuffer | string) => {
      try {
        if (typeof raw === "string") {
          handleText(raw);
          return;
        }
        const buf = Buffer.isBuffer(raw) ? raw : Buffer.from(raw);
        handleBinary(buf);
      } catch (err) {
        console.error("websocket message error", err);
        socket.send(
          serializeMessage(
            createMessage("error", "parse", { data: String(err) })
          )
        );
      }
    });

    function handleText(text: string) {
      const msg = parseMessage(text);

      if (msg.type === "session" && msg.action === "claim") {
        const status = (msg.data ?? {}) as { device?: unknown };
        const deviceInfo = (status.device ?? {
          type: "client",
          deviceName: "unknown",
          deviceInfo: "",
          platform: "unknown",
          machine: "unknown",
          appVersion: "unknown",
          screenResolution: "unknown",
        }) as {
          type: string;
          deviceName: string;
          deviceInfo: string;
          platform: string;
          machine: string;
          appVersion: string;
          screenResolution: string;
        };

        deviceId = `${deviceInfo.deviceName}-${Date.now()}`;
        deviceManager.register(deviceId, socket, { device: deviceInfo });
        socket.send(
          serializeMessage(
            createMessage("session", "claimed", { to: deviceId, data: { id: deviceId } })
          )
        );
        return;
      }

      if (!deviceId) {
        socket.send(
          serializeMessage(
            createMessage("error", "claim", { data: "please send session/claim first" })
          )
        );
        socket.close(1008, "claim required");
        return;
      }

      if (msg.type === "system" && msg.action === "ping") {
        deviceManager.updatePing(deviceId);
        socket.send(
          serializeMessage(
            createMessage("system", "pong", {
              to: deviceId,
              data: { time: Date.now() },
            })
          )
        );
        return;
      }

      if (msg.binary) {
        pendingBinaryMessage = msg;
        return;
      }

      // Persist image/screenshot data embedded as base64 in JSON payloads.
      if ((msg.type === "response" || msg.type === "stream") && typeof msg.data === "object" && msg.data !== null) {
        const dataObj = msg.data as Record<string, unknown>;
        const base64Data = dataObj.__data__;
        if (typeof base64Data === "string" && base64Data.length > 0) {
          try {
            saveScreenshot(deviceId, msg.action, base64Data);
          } catch (err) {
            console.error("failed to save screenshot from payload", err);
          }
        }
      }

      // Forward response/error/stream to dashboard via SSE
      deviceManager.emit(deviceId, { type: msg.type, payload: msg });
    }

    function handleBinary(raw: Buffer) {
      // Some clients send JSON text frames as binary; try parsing as text first.
      const text = raw.toString("utf8");
      if (text.length > 0 && (text[0] === "{" || text[0] === "[" || text[0] === "\"")) {
        try {
          handleText(text);
          return;
        } catch {
          // Not valid JSON text, treat as binary below.
        }
      }

      if (!deviceId) {
        socket.send(
          serializeMessage(
            createMessage("error", "claim", { data: "please send session/claim before binary data" })
          )
        );
        return;
      }
      if (pendingBinaryMessage) {
        pendingBinaryMessage.binaryData = raw;
        // Persist image/screenshot data when it arrives as binary attachment.
        if (pendingBinaryMessage.type === "response" || pendingBinaryMessage.type === "stream") {
          try {
            saveScreenshot(deviceId, pendingBinaryMessage.action, raw);
          } catch (err) {
            console.error("failed to save screenshot", err);
          }
        }
        deviceManager.emit(deviceId, {
          type: pendingBinaryMessage.type,
          payload: pendingBinaryMessage,
        });
        pendingBinaryMessage = null;
      } else {
        deviceManager.emit(deviceId, { type: "binary", payload: { data: raw.toString("base64") } });
      }
    }

    socket.on("close", cleanup);
    socket.on("error", (err) => {
      console.error("socket error", err);
      cleanup();
    });
  });

  httpServer.listen(port, () => {
    console.log(`SmartPlayBuddy server listening on port ${port}`);
  });

  return httpServer;
}
