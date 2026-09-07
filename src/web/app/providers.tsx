"use client";

import React from "react";
import { AppShell } from "../components/layout/app-shell";
import { DevelopmentSessionProvider } from "../features/session/development-session";

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <DevelopmentSessionProvider>
      <AppShell>{children}</AppShell>
    </DevelopmentSessionProvider>
  );
}
