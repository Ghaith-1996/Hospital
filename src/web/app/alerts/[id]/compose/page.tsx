import { redirect } from "next/navigation";

export default function LegacyAlertRoute() {
  redirect("/alerts/new");
}
