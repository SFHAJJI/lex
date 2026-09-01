// The browser entry. Bundled to /client.js and loaded by the hydrated page.

import { attach } from './client.jsx';
import { hydrationTree } from './hydration-proof.jsx';

attach(document.getElementById('hydration-root'), hydrationTree());
