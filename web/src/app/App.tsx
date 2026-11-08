import { apiBaseUrl } from '../api/config';
import { ServerWakeGate } from '../components/ServerWakeGate';

/** Placeholder until PR 7 adds the router and the first screens. */
export function App() {
  return (
    <main>
      <h1>ResuMatch</h1>
      <ServerWakeGate baseUrl={apiBaseUrl}>
        <p>Sunucu hazır. Ekranlar yakında burada olacak.</p>
      </ServerWakeGate>
    </main>
  );
}
