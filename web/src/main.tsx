import { createRoot } from "react-dom/client";
import App from "./App";
import AssistantController from "./AssistantController";
import "./styles.css";

const el = document.getElementById("workspace");
if (el) createRoot(el).render(<App />);
else {
  const assistant = document.getElementById("assistant-root");
  if (assistant) createRoot(assistant).render(<AssistantController standalone />);
}
