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

interface ImagePayload {
  __data__?: string;
  __mime__?: string;
}

function isImagePayload(data: unknown): data is ImagePayload {
  return (
    typeof data === "object" &&
    data !== null &&
    "__data__" in data &&
    typeof (data as ImagePayload).__data__ === "string"
  );
}

function getEventPreview(event: EventItem): { text?: string; image?: string; mime?: string } {
  const payload = event.payload as Record<string, unknown> | undefined;
  const data = payload?.data ?? payload;

  if (event.type === "chat") {
    return { text: String(data ?? "") };
  }

  if (isImagePayload(data)) {
    return {
      image: `data:${data.__mime__ ?? "image/png"};base64,${data.__data__}`,
      mime: data.__mime__,
    };
  }

  return {};
}

export function EventLog({ events }: EventLogProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [events]);

  return (
    <ScrollArea className="h-96 rounded-md border p-4">
      <div className="space-y-3">
        {events.map((event) => {
          const preview = getEventPreview(event);
          return (
            <div key={event.id} className="text-xs border-b pb-3 last:border-0">
              <div className="flex items-center gap-2 text-muted-foreground mb-1">
                <span className="font-semibold text-primary">{event.type}</span>
                <span>{event.time}</span>
              </div>
              {preview.text && (
                <div className="bg-muted rounded-md px-3 py-2 text-sm break-all">
                  {preview.text}
                </div>
              )}
              {preview.image && (
                <a
                  href={preview.image}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-block mt-1"
                >
                  <img
                    src={preview.image}
                    alt="screenshot"
                    className="max-h-48 rounded border object-contain"
                  />
                </a>
              )}
              {!preview.text && !preview.image && (
                <pre className="overflow-x-auto whitespace-pre-wrap break-all">
                  {JSON.stringify(event.payload, null, 2)}
                </pre>
              )}
            </div>
          );
        })}
        <div ref={bottomRef} />
      </div>
    </ScrollArea>
  );
}
