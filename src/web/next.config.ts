import path from "node:path";
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  outputFileTracingRoot: path.join(__dirname, "../.."),
  reactStrictMode: true,
  async rewrites() {
    const apiBase = process.env.CRITICAL_ALERTS_API_URL ?? "http://127.0.0.1:5080";
    if (apiBase.length === 0) {
      return [];
    }

    return [
      {
        source: "/api/v1/:path*",
        destination: `${apiBase}/api/v1/:path*`,
      },
    ];
  },
};

export default nextConfig;
