import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Critical Alerts Platform",
  description: "Phase 6 simulation shell for manual recipient selection and exact alert review.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
