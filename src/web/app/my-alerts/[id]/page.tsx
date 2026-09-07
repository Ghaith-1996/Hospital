import { PractitionerAlert } from "../../../features/connected/practitioner-alerts";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; return <PractitionerAlert alertId={id} />;
}
