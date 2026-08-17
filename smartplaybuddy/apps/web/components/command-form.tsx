"use client";

import { useState } from "react";
import { Button } from "@workspace/ui/components/button";
import { Input } from "@workspace/ui/components/input";
import { Label } from "@workspace/ui/components/label";
import { Textarea } from "@workspace/ui/components/textarea";
import { sendCommand } from "@/lib/api";

interface CommandFormProps {
  deviceId?: string;
  onSent?: (action: string, data: unknown) => void;
}

export function CommandForm({ deviceId, onSent }: CommandFormProps) {
  const [action, setAction] = useState("screen");
  const [data, setData] = useState('{"operate":"capture"}');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!deviceId) return;

    let parsed: unknown = {};
    try {
      parsed = JSON.parse(data || "{}");
    } catch {
      alert("Data 必须是合法 JSON");
      return;
    }

    setLoading(true);
    try {
      await sendCommand(deviceId, action, parsed);
      onSent?.(action, parsed);
    } catch (err) {
      alert(String(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <Label htmlFor="action">Action</Label>
        <Input
          id="action"
          value={action}
          onChange={(e) => setAction(e.target.value)}
          placeholder="screen"
        />
      </div>
      <div>
        <Label htmlFor="data">Data (JSON)</Label>
        <Textarea
          id="data"
          value={data}
          onChange={(e) => setData(e.target.value)}
          rows={5}
          placeholder='{"operate":"capture"}'
        />
      </div>
      <Button type="submit" disabled={!deviceId || loading}>
        {loading ? "发送中..." : "发送指令"}
      </Button>
    </form>
  );
}
