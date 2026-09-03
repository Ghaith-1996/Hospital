import React from "react";

export type TabItem<TValue extends string> = {
  value: TValue;
  label: string;
  count?: number;
};

export function Tabs<TValue extends string>({
  tabs,
  value,
  onChange,
  ariaLabel,
}: {
  tabs: TabItem<TValue>[];
  value: TValue;
  onChange: (value: TValue) => void;
  ariaLabel: string;
}) {
  const refs = React.useRef<Array<HTMLButtonElement | null>>([]);
  const [focusedValue, setFocusedValue] = React.useState<TValue>(value);

  React.useEffect(() => {
    setFocusedValue(value);
  }, [value]);

  function moveFocus(currentIndex: number, direction: 1 | -1) {
    const nextIndex = (currentIndex + direction + tabs.length) % tabs.length;
    setFocusedValue(tabs[nextIndex].value);
    refs.current[nextIndex]?.focus();
  }

  return (
    <div className="tabs" role="tablist" aria-label={ariaLabel}>
      {tabs.map((tab, index) => (
        <button
          className="tabs__tab"
          key={tab.value}
          ref={(node) => {
            refs.current[index] = node;
          }}
          type="button"
          role="tab"
          aria-selected={tab.value === value}
          tabIndex={tab.value === focusedValue ? 0 : -1}
          onClick={() => {
            setFocusedValue(tab.value);
            onChange(tab.value);
          }}
          onFocus={() => setFocusedValue(tab.value)}
          onKeyDown={(event) => {
            if (event.key === "ArrowRight") {
              event.preventDefault();
              moveFocus(index, 1);
            }
            if (event.key === "ArrowLeft") {
              event.preventDefault();
              moveFocus(index, -1);
            }
          }}
        >
          <span>{tab.label}</span>
          {tab.count !== undefined ? <span className="tabs__count">{tab.count}</span> : null}
        </button>
      ))}
    </div>
  );
}
