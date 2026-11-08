import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { apiBaseUrl } from './api/config';
import { App } from './app/App';
import { AuthSession } from './auth/session';
import './styles/global.css';

const container = document.getElementById('root');
if (!container) {
  throw new Error('The #root element is missing from index.html.');
}

// One session per tab; the access token lives only inside it.
const session = new AuthSession({ baseUrl: apiBaseUrl });

createRoot(container).render(
  <StrictMode>
    <App session={session} />
  </StrictMode>,
);
