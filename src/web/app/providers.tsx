"use client";

import React from "react";
import { AppShell } from "../components/layout/app-shell";
import { PrototypeProvider } from "../features/alerts/prototype-store";
import { DevelopmentSessionProvider } from "../features/session/development-session";

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <DevelopmentSessionProvider><PrototypeProvider>
      <AppShell>{children}</AppShell>
    </PrototypeProvider></DevelopmentSessionProvider>
  );
}
