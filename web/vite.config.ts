import { defineConfig } from "vite";

export default defineConfig({
  server: {
    port: 5173,       // the WinForms app will point WebView2 here during development
    strictPort: true, // fail instead of silently switching to 5174 if the port is busy
  },
  build: {
    target: "es2022", // needed for BigInt (the proto's uint64 handles)
  },
});