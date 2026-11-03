import {StrictMode} from "react";
import {createRoot} from "react-dom/client";
import {LocalizationProvider} from "@photino-net-utils/bridge";
import "./globals.scss";
import "@fontsource/inter/latin-400.css";
import "@fontsource/inter/latin-500.css";
import "@fontsource/inter/latin-600.css";
import "@fontsource/inter/latin-700.css";
import "@fontsource/jetbrains-mono/latin-400.css";
import "@fontsource/jetbrains-mono/latin-500.css";
import App from "./App";

const rootElement = document.getElementById("root");
if (rootElement)
    createRoot(rootElement).render(
        <StrictMode>
            <LocalizationProvider>
                <App/>
            </LocalizationProvider>
        </StrictMode>
    );
