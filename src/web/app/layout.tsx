import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Critical Alerts Platform",
  description: "Phase 1 local platform scaffold for a fictional healthcare simulation.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
