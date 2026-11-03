import {defineConfig} from "vite";
import react from "@vitejs/plugin-react";

// The page is served from a project Pages URL (`/ClipboardSync/`), so the asset paths stay relative.
export default defineConfig({
    plugins: [react()],
    base: "./"
});
