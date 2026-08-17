"use client";

import { useEffect, useRef } from "react";
import { ScrollArea } from "@workspace/ui/components/scroll-area";

export interface EventItem {
  id: string;
  type: string;
  time: string;
  payload: unknown;
}

interface EventLogProps {
  events: EventItem[];
}

export function EventLog({ events }: EventLogProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [events]);

  return (
    <ScrollArea className="h-96 rounded-md border p-4">
      <div className="space-y-2">
        {events.map((event) => (
          <div key={event.id} className="text-xs border-b pb-2 last:border-0">
            <div className="flex items-center gap-2 text-muted-foreground">
              <span className="font-semibold text-primary">{event.type}</span>
              <span>{event.time}</span>
            </div>
            <pre className="mt-1 overflow-x-auto whitespace-pre-wrap break-all">
              {JSON.stringify(event.payload, null, 2)}
            </pre>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>
    </ScrollArea>
  );
}
