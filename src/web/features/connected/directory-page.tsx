"use client";
import React from "react";
import Link from "next/link";
import { searchDirectory } from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading, useServerQuery } from "./common";
import { DirectoryBrowser } from "./directory-browser";
const loadDirectory = () => searchDirectory({ includeInactive: true });
export function DirectoryPage() {
  const query = useServerQuery(loadDirectory);
  return <><PageHeader title="Directory" description="Authoritative fictional practitioner directory." actions={<Link href="/directory/import" className="button-secondary">Import fictional CSV</Link>} /><ApiError error={query.error} retry={query.reload} />{query.loading ? <Loading /> : query.data && <DirectoryBrowser initial={query.data} />}</>;
}
