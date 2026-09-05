"use client";

import React from "react";
import { AlertList } from "../../components/alerts/alert-list";
import { PageHeader } from "../../components/ui/page-header";
import { ScreenState } from "../../components/ui/screen-state";
import { Tabs } from "../../components/ui/tabs";
import { selectAlerts } from "../../features/alerts/selectors";
import { usePrototype } from "../../features/alerts/prototype-store";
import type { AlertFilters, AlertRecord, AlertStatus } from "../../features/alerts/types";

type StatusTab = "all" | "draft" | "sent" | "in-progress" | "resolved" | "cancelled";
type DateWindow = "" | "last-hour" | "last-four-hours" | "today";

const emptyFilters: AlertFilters = {};
const statusTabLabels: Record<StatusTab, string> = {
  all: "All",
  draft: "Draft",
  sent: "Sent",
  "in-progress": "In Progress",
  resolved: "Resolved",
  cancelled: "Cancelled",
};

function sameFilters(left: AlertFilters, right: AlertFilters) {
  return (
    left.status === right.status &&
    left.urgency === right.urgency &&
    left.department === right.department &&
    left.updatedAfter === right.updatedAfter
  );
}

function countActiveFilters(filters: AlertFilters) {
  return [filters.status, filters.urgency, filters.department, filters.updatedAfter].filter(Boolean).length;
}

function getDateWindow(value: DateWindow): string | undefined {
  if (value === "") return undefined;
  if (value === "last-hour") return "2026-08-30T13:24:00.000Z";
  if (value === "last-four-hours") return "2026-08-30T10:24:00.000Z";
  return "2026-08-30T00:00:00.000Z";
}

function getStatusTabAlerts(alerts: AlertRecord[], statusTab: StatusTab) {
  if (statusTab === "all") return alerts;
  if (statusTab === "in-progress") {
    return alerts.filter((alert) => alert.status === "in-progress" || alert.status === "escalating");
  }
  return alerts.filter((alert) => alert.status === statusTab);
}

function readDepartmentOptions(alerts: AlertRecord[]) {
  return Array.from(new Set(alerts.map((alert) => alert.department))).sort((left, right) => left.localeCompare(right));
}

function getEmptyStateContent(statusTab: StatusTab, filters: AlertFilters, hasAnyAlerts: boolean) {
  const hasFilters = !sameFilters(filters, emptyFilters);

  if (statusTab !== "all") {
    const tabLabel = statusTabLabels[statusTab].toLowerCase();
    return hasFilters
      ? {
          label: `No ${tabLabel} alerts match these filters.`,
          description: "Try clearing the current filters or choosing a different status tab.",
          showClearAction: true,
        }
      : {
          label: `No ${tabLabel} alerts yet.`,
          description: hasAnyAlerts
            ? "Other fictional alerts exist, but none are in the currently selected tab."
            : "This local overview will show fictional alerts once they are created.",
          showClearAction: false,
        };
  }

  return hasFilters
    ? {
        label: "No alerts match these filters.",
        description: "Try clearing or changing the current filters to see other fictional alerts.",
        showClearAction: true,
      }
    : {
        label: "No alerts are available.",
        description: "This local overview will show fictional alerts once they are created.",
        showClearAction: false,
      };
}

export default function AlertsOverviewPage() {
  const { state } = usePrototype();
  const [statusTab, setStatusTab] = React.useState<StatusTab>("all");
  const [draftFilters, setDraftFilters] = React.useState<AlertFilters>(emptyFilters);
  const [appliedFilters, setAppliedFilters] = React.useState<AlertFilters>(emptyFilters);
  const [filtersOpen, setFiltersOpen] = React.useState(false);

  const filteredAlerts = selectAlerts(state, appliedFilters);
  const visibleAlerts = getStatusTabAlerts(filteredAlerts, statusTab);
  const departmentOptions = readDepartmentOptions(state.alerts);
  const activeFilterCount = countActiveFilters(appliedFilters);
  const emptyState = getEmptyStateContent(statusTab, appliedFilters, state.alerts.length > 0);
  const tabs = [
    { value: "all", label: "All" },
    { value: "draft", label: "Draft" },
    { value: "sent", label: "Sent" },
    { value: "in-progress", label: "In Progress" },
    { value: "resolved", label: "Resolved" },
    { value: "cancelled", label: "Cancelled" },
  ] as const;

  function updateDraftFilters(patch: Partial<AlertFilters>) {
    setDraftFilters((current) => ({ ...current, ...patch }));
  }

  function applyFilters() {
    setAppliedFilters(draftFilters);
    setFiltersOpen(false);
  }

  function clearFilters() {
    setDraftFilters(emptyFilters);
    setAppliedFilters(emptyFilters);
    setFiltersOpen(false);
  }

  const filterButtonLabel = activeFilterCount === 0 ? "Filters" : `Filters ${activeFilterCount} active filter${activeFilterCount === 1 ? "" : "s"}`;

  return (
    <section className="alerts-overview">
      <PageHeader
        title="Alerts"
        description="Track fictional alerts across draft, sent, response, and resolution states."
        actions={
          <button
            type="button"
            className="button-secondary alerts-overview__filter-button"
            aria-expanded={filtersOpen}
            aria-controls="alerts-filter-drawer"
            aria-label={filterButtonLabel}
            onClick={() => setFiltersOpen((current) => !current)}
          >
            Filters
            {activeFilterCount > 0 ? <span className="alerts-overview__filter-count">{activeFilterCount}</span> : null}
          </button>
        }
      />

      <div className="alerts-overview__tabs">
        <Tabs ariaLabel="Alert status tabs" tabs={[...tabs]} value={statusTab} onChange={setStatusTab} />
      </div>

      {filtersOpen ? (
        <section className="alerts-filter-drawer" id="alerts-filter-drawer" aria-label="Alert filters">
          <div className="alerts-filter-drawer__grid">
            <label className="filter-field" htmlFor="alert-filter-urgency">
              Urgency
              <select
                id="alert-filter-urgency"
                value={draftFilters.urgency ?? ""}
                onChange={(event) =>
                  updateDraftFilters({
                    urgency: event.target.value === "" ? undefined : (event.target.value as AlertFilters["urgency"]),
                  })
                }
              >
                <option value="">All urgencies</option>
                <option value="critical">Critical</option>
                <option value="high">High</option>
                <option value="routine">Routine</option>
              </select>
            </label>

            <label className="filter-field" htmlFor="alert-filter-status">
              Status
              <select
                id="alert-filter-status"
                value={draftFilters.status ?? ""}
                onChange={(event) =>
                  updateDraftFilters({
                    status: event.target.value === "" ? undefined : (event.target.value as AlertStatus),
                  })
                }
              >
                <option value="">All statuses</option>
                <option value="draft">Draft</option>
                <option value="sent">Sent</option>
                <option value="in-progress">In Progress</option>
                <option value="resolved">Resolved</option>
                <option value="cancelled">Cancelled</option>
                <option value="escalating">Escalating</option>
              </select>
            </label>

            <label className="filter-field" htmlFor="alert-filter-date-window">
              Date Window
              <select
                id="alert-filter-date-window"
                value={
                  draftFilters.updatedAfter === undefined
                    ? ""
                    : draftFilters.updatedAfter === "2026-08-30T13:24:00.000Z"
                      ? "last-hour"
                      : draftFilters.updatedAfter === "2026-08-30T10:24:00.000Z"
                        ? "last-four-hours"
                        : "today"
                }
                onChange={(event) =>
                  updateDraftFilters({
                    updatedAfter: getDateWindow(event.target.value as DateWindow),
                  })
                }
              >
                <option value="">Any time</option>
                <option value="last-hour">Last hour</option>
                <option value="last-four-hours">Last 4 hours</option>
                <option value="today">Today</option>
              </select>
            </label>

            <label className="filter-field" htmlFor="alert-filter-department">
              Department
              <select
                id="alert-filter-department"
                value={draftFilters.department ?? ""}
                onChange={(event) =>
                  updateDraftFilters({
                    department: event.target.value === "" ? undefined : event.target.value,
                  })
                }
              >
                <option value="">All departments</option>
                {departmentOptions.map((department) => (
                  <option key={department} value={department}>
                    {department}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="form-actions">
            <button type="button" className="button-secondary" onClick={clearFilters}>
              Clear filters
            </button>
            <button type="button" onClick={applyFilters}>
              Apply filters
            </button>
          </div>
        </section>
      ) : null}

      {visibleAlerts.length > 0 ? (
        <AlertList alerts={visibleAlerts} />
      ) : (
        <ScreenState
          kind="empty"
          label={emptyState.label}
          description={emptyState.description}
          headingLevel="h2"
          action={
            emptyState.showClearAction ? (
              <button type="button" className="button-secondary" onClick={clearFilters}>
                Clear filters
              </button>
            ) : null
          }
        />
      )}
    </section>
  );
}
