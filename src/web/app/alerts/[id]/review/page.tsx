import { ReviewAlert } from "../../../../features/connected/review-alert";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; return <ReviewAlert alertId={id} />;
}
