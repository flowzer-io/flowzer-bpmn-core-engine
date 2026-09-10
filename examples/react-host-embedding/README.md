# React-Host-Referenz

Diese absichtlich schmucklose, nur kompilierte Fixture zeigt die äußere
Integrationsgrenze von Flowzer. Sie importiert ausschließlich `@flowzer/sdk`,
`@flowzer/react`, React und TanStack Query – niemals Quellcode der Flowzer Console.

Der Host liefert pro Request ein auf Flowzer begrenztes Benutzertoken und einen bei
jeder Anmeldung wechselnden, nicht geheimen Sitzungsscope. Token Exchange, Fachobjekte,
Routing, Styling und der konkrete Formularrenderer bleiben außerhalb von Flowzer.

```bash
npm --prefix ../../packages/flowzer-sdk run build
npm --prefix ../../packages/flowzer-react run build
npm ci
npm run typecheck
```

Die Fixture ist keine fertige Oberfläche und kein Deploymentbeispiel. Ihr Zweck ist ein
CI-gesicherter Beleg, dass eine unabhängige Host-Anwendung mit den öffentlichen Paketen
kompilieren kann.
