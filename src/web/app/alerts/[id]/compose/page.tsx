import { ComposeAlert } from "../../../../features/connected/compose-alert";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; return <ComposeAlert alertId={id} />;
}
