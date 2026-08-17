"use client";

import { useEffect, useState } from "react";
import { Button } from "@workspace/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@workspace/ui/components/card";
import { DeviceList } from "@/components/device-list";
import { CommandForm } from "@/components/command-form";
import { EventLog, type EventItem } from "@/components/event-log";
import { getDevices, subscribeEvents, type Device } from "@/lib/api";

export default function DashboardPage() {
  const [devices, setDevices] = useState<Device[]>([]);
  const [selectedId, setSelectedId] = useState<string>();
  const [events, setEvents] = useState<EventItem[]>([]);
  const [loading, setLoading] = useState(false);

  async function refreshDevices() {
    setLoading(true);
    try {
      const list = await getDevices();
      setDevices(list);
      if (list.length > 0 && !selectedId) {
        setSelectedId(list[0]!.id);
      }
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refreshDevices();
    const interval = setInterval(refreshDevices, 3000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!selectedId) return;
    setEvents([]);
    return subscribeEvents(selectedId, {
      onConnected: (payload) => pushEvent("connected", payload),
      onResponse: (payload) => pushEvent("response", payload),
      onStream: (payload) => pushEvent("stream", payload),
      onError: (payload) => pushEvent("error", payload),
      onDisconnected: (payload) => pushEvent("disconnected", payload),
      onBinary: (payload) => pushEvent("binary", payload),
    });
  }, [selectedId]);

  function pushEvent(type: string, payload: unknown) {
    setEvents((prev) => [
      ...prev,
      {
        id: `${Date.now()}-${Math.random()}`,
        type,
        time: new Date().toLocaleTimeString(),
        payload,
      },
    ]);
  }

  return (
    <main className="container mx-auto p-6 grid grid-cols-1 md:grid-cols-3 gap-6">
      <div className="md:col-span-1 space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold">在线设备</h2>
          <Button size="sm" onClick={refreshDevices} disabled={loading}>
            刷新
          </Button>
        </div>
        <DeviceList devices={devices} selectedId={selectedId} onSelect={setSelectedId} />
      </div>

      <div className="md:col-span-2 space-y-4">
        <Card>
          <CardHeader>
            <CardTitle>下发指令</CardTitle>
          </CardHeader>
          <CardContent>
            <CommandForm deviceId={selectedId} onSent={(action) => pushEvent("sent", { action })} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>事件日志</CardTitle>
          </CardHeader>
          <CardContent>
            <EventLog events={events} />
          </CardContent>
        </Card>
      </div>
    </main>
  );
}
