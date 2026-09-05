import { redirect } from "next/navigation";

export default async function LegacyAlertLivePage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  redirect(`/alerts/${encodeURIComponent(id)}`);
}
