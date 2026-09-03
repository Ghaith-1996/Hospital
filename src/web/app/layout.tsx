import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Critical Alerts Platform",
  description: "Simulation-only critical alerts workflow with practitioner response and operator status views.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
