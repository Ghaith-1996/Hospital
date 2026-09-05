import { RecipientSelection } from "../../../../features/connected/recipient-selection";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; return <RecipientSelection alertId={id} />;
}
