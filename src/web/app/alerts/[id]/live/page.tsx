import { LiveAlert } from "../../../../features/connected/live-alert";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; return <LiveAlert alertId={id} />;
}
