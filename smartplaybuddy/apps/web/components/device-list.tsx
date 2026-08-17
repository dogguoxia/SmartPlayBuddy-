"use client";

import { Card, CardContent, CardHeader, CardTitle } from "@workspace/ui/components/card";
import { Badge } from "@workspace/ui/components/badge";
import type { Device } from "@/lib/api";

interface DeviceListProps {
  devices: Device[];
  selectedId?: string;
  onSelect: (id: string) => void;
}

export function DeviceList({ devices, selectedId, onSelect }: DeviceListProps) {
  return (
    <div className="space-y-2">
      {devices.map((device) => (
        <Card
          key={device.id}
          className={`cursor-pointer transition-colors ${
            selectedId === device.id ? "border-primary" : "hover:bg-muted"
          }`}
          onClick={() => onSelect(device.id)}
        >
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium flex items-center justify-between">
              {device.info.deviceName}
              <Badge variant="outline">{device.info.type}</Badge>
            </CardTitle>
          </CardHeader>
          <CardContent className="text-xs text-muted-foreground space-y-1">
            <p>平台：{device.info.platform}</p>
            <p>分辨率：{device.info.screenResolution}</p>
            <p>版本：{device.info.appVersion}</p>
          </CardContent>
        </Card>
      ))}
      {devices.length === 0 && (
        <p className="text-sm text-muted-foreground">暂无在线设备</p>
      )}
    </div>
  );
}
