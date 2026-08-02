import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";

const el = document.getElementById("workspace");
if (el) createRoot(el).render(<App />);
