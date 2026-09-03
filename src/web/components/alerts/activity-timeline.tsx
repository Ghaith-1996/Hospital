import React from "react";
import type { AlertActivity } from "../../features/alerts/types";

function formatTimelineTime(value: string) {
  return new Intl.DateTimeFormat("en-US", {
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
  }).format(new Date(value));
}

function readActivitySequence(id: string) {
  const parsed = id.match(/-seq(\d+)-/u)?.[1];
  return parsed ? Number(parsed) : 0;
}

export function ActivityTimeline({ activities }: { activities?: AlertActivity[] }) {
  const sortedActivities = [...(activities ?? [])].sort((left, right) => {
    const timeComparison = left.occurredAt.localeCompare(right.occurredAt);
    if (timeComparison !== 0) return timeComparison;
    return readActivitySequence(left.id) - readActivitySequence(right.id);
  });

  return (
    <section className="activity-timeline detail-card" role="region" aria-label="Activity Timeline">
      <div className="section-heading">
        <h2>Activity Timeline</h2>
      </div>
      {sortedActivities.length > 0 ? (
        <ol className="timeline-list" aria-label="Activity Timeline">
          {sortedActivities.map((activity) => (
            <li className={`timeline-item timeline-item--${activity.tone}`} key={activity.id}>
              <time dateTime={activity.occurredAt}>{formatTimelineTime(activity.occurredAt)}</time>
              <span>{activity.label}</span>
            </li>
          ))}
        </ol>
      ) : (
        <p className="empty-note">No fictional activity has been recorded for this alert.</p>
      )}
    </section>
  );
}
