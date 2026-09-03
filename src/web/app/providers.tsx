"use client";

import React from "react";
import { AppShell } from "../components/layout/app-shell";
import { PrototypeProvider } from "../features/alerts/prototype-store";

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <PrototypeProvider>
      <AppShell>{children}</AppShell>
    </PrototypeProvider>
  );
}
