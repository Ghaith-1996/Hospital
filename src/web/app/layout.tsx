import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Critical Alerts Platform",
  description: "Phase 3 simulation shell with fictional development authentication.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
