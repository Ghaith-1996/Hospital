"use client";

import React from "react";
import { useRouter } from "next/navigation";
import { ScreenState } from "../components/ui/screen-state";
import { usePrototype } from "../features/alerts/prototype-store";

export default function HomePage() {
  const router = useRouter();
  const { hydrated, state } = usePrototype();
  const currentUser = state.users.find((user) => user.id === state.selectedUserId) ?? state.users[0];

  React.useEffect(() => {
    if (!hydrated) return;
    router.replace(currentUser.role === "doctor" ? "/my-alerts" : "/alerts/new");
  }, [currentUser.role, hydrated, router]);

  return <ScreenState kind="loading" label="Loading fictional demo workspace" />;
}
