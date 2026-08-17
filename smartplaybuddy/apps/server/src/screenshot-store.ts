import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const SCREENSHOTS_DIR = join(__dirname, "..", "screenshots");

function ensureDir() {
  if (!existsSync(SCREENSHOTS_DIR)) {
    mkdirSync(SCREENSHOTS_DIR, { recursive: true });
  }
}

function sanitizeDeviceId(id: string): string {
  return id.replace(/[^a-zA-Z0-9_-]/g, "_").slice(0, 64);
}

function detectExtension(data: Buffer): string {
  // PNG magic
  if (data.length >= 8 && data[0] === 0x89 && data[1] === 0x50) return "png";
  // JPEG magic
  if (data.length >= 3 && data[0] === 0xff && data[1] === 0xd8) return "jpg";
  // BMP magic
  if (data.length >= 2 && data[0] === 0x42 && data[1] === 0x4d) return "bmp";
  // WebP magic
  if (data.length >= 12 && data.toString("ascii", 8, 12) === "WEBP") return "webp";
  return "png";
}

export interface SavedScreenshot {
  id: string;
  deviceId: string;
  action: string;
  filename: string;
  path: string;
  size: number;
  createdAt: number;
}

export function saveScreenshot(
  deviceId: string,
  action: string,
  data: Buffer | string
): SavedScreenshot {
  ensureDir();

  const buf = typeof data === "string" ? Buffer.from(data, "base64") : data;
  const ext = detectExtension(buf);
  const safeDevice = sanitizeDeviceId(deviceId);
  const timestamp = Date.now();
  const filename = `${safeDevice}_${action}_${timestamp}.${ext}`;
  const filePath = join(SCREENSHOTS_DIR, filename);

  writeFileSync(filePath, buf);

  return {
    id: filename,
    deviceId,
    action,
    filename,
    path: filePath,
    size: buf.length,
    createdAt: timestamp,
  };
}

export function listScreenshots(deviceId?: string): SavedScreenshot[] {
  if (!existsSync(SCREENSHOTS_DIR)) return [];

  const entries = readdirSync(SCREENSHOTS_DIR);
  const screenshots: SavedScreenshot[] = [];

  for (const filename of entries) {
    if (!filename.match(/\.(png|jpg|jpeg|bmp|webp)$/i)) continue;
    const filePath = join(SCREENSHOTS_DIR, filename);
    const parts = filename.split("_");
    const safeDevice = parts[0] ?? "unknown";
    const action = parts[1] ?? "unknown";

    if (deviceId && safeDevice !== sanitizeDeviceId(deviceId)) continue;

    let size = 0;
    let createdAt = Date.now();
    try {
      const s = statSync(filePath);
      size = s.size;
      createdAt = Math.floor(s.birthtimeMs);
    } catch {
      // ignore
    }

    screenshots.push({
      id: filename,
      deviceId: safeDevice,
      action,
      filename,
      path: filePath,
      size,
      createdAt,
    });
  }

  return screenshots.sort((a, b) => b.createdAt - a.createdAt);
}

export function getScreenshotPath(id: string): string | null {
  if (!id || id.includes("..") || id.includes("/") || id.includes("\\")) return null;
  const filePath = join(SCREENSHOTS_DIR, id);
  if (!existsSync(filePath)) return null;
  return filePath;
}

export function readScreenshot(id: string): Buffer | null {
  const filePath = getScreenshotPath(id);
  if (!filePath) return null;
  return readFileSync(filePath);
}
