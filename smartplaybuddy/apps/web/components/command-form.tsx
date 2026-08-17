"use client";

import { useState } from "react";
import { Button } from "@workspace/ui/components/button";
import { Input } from "@workspace/ui/components/input";
import { Label } from "@workspace/ui/components/label";
import { sendCommand, sendChatMessage } from "@/lib/api";

interface CommandFormProps {
  deviceId?: string;
  onSent?: (action: string, data: unknown) => void;
}

type Mode = "text" | "screenshot";

export function CommandForm({ deviceId, onSent }: CommandFormProps) {
  const [mode, setMode] = useState<Mode>("text");
  const [text, setText] = useState("");
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!deviceId) return;

    setLoading(true);
    try {
      if (mode === "text") {
        if (!text.trim()) return;
        await sendChatMessage(deviceId, text.trim());
        onSent?.("chat", text.trim());
        setText("");
      } else {
        await sendCommand(deviceId, "screen", { operate: "capture" });
        onSent?.("screen", { operate: "capture" });
      }
    } catch (err) {
      alert(String(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <Label htmlFor="mode">行为</Label>
        <select
          id="mode"
          value={mode}
          onChange={(e) => setMode(e.target.value as Mode)}
          className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm"
        >
          <option value="text">发送文本</option>
          <option value="screenshot">请求截图</option>
        </select>
      </div>

      {mode === "text" ? (
        <div>
          <Label htmlFor="text">文字内容</Label>
          <Input
            id="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="输入要发送给客户端的文字"
          />
        </div>
      ) : (
        <div className="text-sm text-muted-foreground">
          点击发送后，客户端会截取当前屏幕并回传。
        </div>
      )}

      <Button type="submit" disabled={!deviceId || loading}>
        {loading ? "发送中..." : "发送"}
      </Button>
    </form>
  );
}
