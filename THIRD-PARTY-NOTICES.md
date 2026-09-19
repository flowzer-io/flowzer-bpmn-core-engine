# Abhängigkeits- und Lizenzhinweise (Third-Party Notices)

Diese Datei wird automatisch durch
[`scripts/ci/generate-third-party-notices.py`](scripts/ci/generate-third-party-notices.py)
aus den direkten Abhängigkeiten der Solution und der npm-Projekte erzeugt.
**Nicht von Hand bearbeiten** — Änderungen werden beim nächsten Lauf des Skripts
überschrieben. Die CI erzeugt die Datei neu und lehnt Abweichungen
(`git diff --exit-code`) ab, damit sie nicht veraltet.

Flowzer BPMN Core Engine selbst steht unter der Mozilla Public License 2.0
(siehe [LICENSE](LICENSE)); diese Datei dokumentiert ausschließlich die Lizenzen
**direkter** Drittanbieter-Abhängigkeiten. Transitive Abhängigkeiten sind hier nicht
aufgeführt. Eigene Workspace-Pakete (`@flowzer/react`, `@flowzer/sdk`) sind kein
Drittanbieter-Code und daher ausgenommen.

Ein ⚠️ markiert Pakete, deren Lizenz manuell geprüft werden sollte (nicht ohne
Weiteres als unproblematisch für eine MPL-2.0-Nutzung eingestuft, unklar oder nicht
automatisch ermittelbar). Aktuell 7 von 77
Einträgen.

## .NET (NuGet)

Direkte `<PackageReference>`-Einträge aus allen `*.csproj` der Solution, Lizenz aus
den nuspec-Metadaten des lokalen NuGet-Cache.

| Paket | Version | Lizenz | Verwendet in |
|---|---|---|---|
| coverlet.collector | 8.0.1 | MIT | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| FluentAssertions ⚠️ | 8.9.0 | siehe mitgelieferte Lizenzdatei "LICENSE" im Paket (nicht automatisch als SPDX-Kennung bestimmbar) | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| MailKit | 4.18.0 | MIT | `src/WebApiEngine/WebApiEngine.csproj` |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.11 | MIT | `src/WebApiEngine/WebApiEngine.csproj` |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | 10.0.11 | MIT | `src/WebApiEngine/WebApiEngine.csproj` |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.6 | MIT | `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj` |
| Microsoft.AspNetCore.OpenApi | 10.0.11 | MIT | `src/WebApiEngine/WebApiEngine.csproj` |
| Microsoft.ClearScript.V8 | 7.5.0 | MIT (siehe eingebettete License.txt) | `src/Flowzer.Shared/Flowzer.Shared.csproj`, `src/core-engine/core-engine.csproj` |
| Microsoft.ClearScript.V8.Native.linux-arm64 ⚠️ | 7.5.0 | siehe mitgelieferte Lizenzdatei "License.txt" im Paket (nicht automatisch als SPDX-Kennung bestimmbar) | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| Microsoft.ClearScript.V8.Native.linux-x64 ⚠️ | 7.5.0 | siehe mitgelieferte Lizenzdatei "License.txt" im Paket (nicht automatisch als SPDX-Kennung bestimmbar) | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| Microsoft.ClearScript.V8.Native.osx-arm64 | 7.5.0 | MIT (siehe eingebettete License.txt) | `src/core-engine/core-engine.csproj` |
| Microsoft.ClearScript.V8.Native.osx-x64 ⚠️ | 7.5.0 | siehe mitgelieferte Lizenzdatei "License.txt" im Paket (nicht automatisch als SPDX-Kennung bestimmbar) | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| Microsoft.ClearScript.V8.Native.win-x64 ⚠️ | 7.5.0 | siehe mitgelieferte Lizenzdatei "License.txt" im Paket (nicht automatisch als SPDX-Kennung bestimmbar) | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| Microsoft.Extensions.TimeProvider.Testing | 9.10.0 | MIT | `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj` |
| Microsoft.NET.Test.Sdk | 18.4.0 | MIT | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| Newtonsoft.Json | 13.0.4 | MIT | `src/Model/Model.csproj`, `src/PostgreSqlStorageSystem/PostgreSqlStorageSystem.csproj`, `src/WebApiEngine.Shared/WebApiEngine.Shared.csproj`, `src/core-engine/core-engine.csproj` |
| Npgsql | 10.0.3 | PostgreSQL | `src/PostgreSqlStorageSystem/PostgreSqlStorageSystem.csproj` |
| NUnit | 4.5.1 | MIT | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| NUnit.Analyzers | 4.12.0 | MIT | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| NUnit3TestAdapter | 6.2.0 | MIT | `src/FlowzerDmn.Tests/FlowzerDmn.Tests.csproj`, `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj`, `src/core-engine-tests/core-engine-tests.csproj` |
| OpenTelemetry.Exporter.Console | 1.18.0 | Apache-2.0 | `src/WebApiEngine/WebApiEngine.csproj` |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.18.0 | Apache-2.0 | `src/WebApiEngine/WebApiEngine.csproj` |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.18.0-beta.1 | Apache-2.0 | `src/WebApiEngine/WebApiEngine.csproj` |
| OpenTelemetry.Extensions.Hosting | 1.18.0 | Apache-2.0 | `src/WebApiEngine/WebApiEngine.csproj` |
| OpenTelemetry.Instrumentation.AspNetCore | 1.18.0 | Apache-2.0 | `src/WebApiEngine/WebApiEngine.csproj` |
| Swashbuckle.AspNetCore | 10.2.3 | MIT | `src/WebApiEngine/WebApiEngine.csproj` |
| Testcontainers.PostgreSql | 4.14.0 | MIT | `src/WebApiEngine.Tests/WebApiEngine.Tests.csproj` |

## JavaScript/TypeScript (npm)

Direkte `dependencies`/`devDependencies` aus `src/FlowzerConsole/package.json`,
`packages/flowzer-sdk/package.json` und `packages/flowzer-react/package.json`,
Lizenz aus dem jeweils installierten `node_modules/<paket>/package.json`.

| Paket | Version | Lizenz | Projekt(e) |
|---|---|---|---|
| @eslint/js | 9.39.5 | MIT | `src/FlowzerConsole` |
| @fontsource-variable/bricolage-grotesque | 5.3.0 | OFL-1.1 | `src/FlowzerConsole` |
| @fontsource-variable/hanken-grotesk | 5.3.0 | OFL-1.1 | `src/FlowzerConsole` |
| @fontsource-variable/jetbrains-mono | 5.3.0 | OFL-1.1 | `src/FlowzerConsole` |
| @formio/js | 5.5.2 | MIT | `src/FlowzerConsole` |
| @material-symbols/svg-400 | 0.36.4 | Apache-2.0 | `src/FlowzerConsole` |
| @radix-ui/react-dialog | 1.1.23 | MIT | `src/FlowzerConsole` |
| @radix-ui/react-dropdown-menu | 2.1.24 | MIT | `src/FlowzerConsole` |
| @radix-ui/react-popover | 1.1.23 | MIT | `src/FlowzerConsole` |
| @radix-ui/react-tabs | 1.1.21 | MIT | `src/FlowzerConsole` |
| @radix-ui/react-tooltip | 1.2.16 | MIT | `src/FlowzerConsole` |
| @tailwindcss/vite | 4.3.3 | MIT | `src/FlowzerConsole` |
| @tanstack/react-query | 5.102.8 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| @tanstack/react-query-devtools | 5.102.8 | MIT | `src/FlowzerConsole` |
| @tanstack/react-router | 1.170.32 | MIT | `src/FlowzerConsole` |
| @testing-library/dom | 10.4.1 | MIT | `src/FlowzerConsole` |
| @testing-library/jest-dom | 6.9.1 | MIT | `src/FlowzerConsole` |
| @testing-library/react | 16.3.3 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| @testing-library/user-event | 14.6.7 | MIT | `src/FlowzerConsole` |
| @types/node | 26.4.1 | MIT | `src/FlowzerConsole` |
| @types/node | 26.5.0 | MIT | `packages/flowzer-sdk` |
| @types/react | 19.2.18 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| @types/react-dom | 19.2.7 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| @vitejs/plugin-react | 4.7.0 | MIT | `src/FlowzerConsole` |
| bootstrap-icons | 1.13.1 | MIT | `src/FlowzerConsole` |
| bpmn-auto-layout | 1.3.0 | MIT | `src/FlowzerConsole` |
| bpmn-js ⚠️ | 18.28.0 | SEE LICENSE IN LICENSE | `src/FlowzerConsole` |
| bpmn-moddle | 10.2.0 | MIT | `src/FlowzerConsole` |
| clsx | 2.1.1 | MIT | `src/FlowzerConsole` |
| cmdk | 1.1.1 | MIT | `src/FlowzerConsole` |
| date-fns | 4.4.0 | MIT | `src/FlowzerConsole` |
| dmn-js ⚠️ | 17.10.2 | SEE LICENSE IN LICENSE | `src/FlowzerConsole` |
| eslint | 9.39.5 | MIT | `src/FlowzerConsole` |
| eslint-plugin-react-hooks | 5.2.0 | MIT | `src/FlowzerConsole` |
| eslint-plugin-react-refresh | 0.4.26 | MIT | `src/FlowzerConsole` |
| globals | 16.5.0 | MIT | `src/FlowzerConsole` |
| jsdom | 26.1.0 | MIT | `src/FlowzerConsole` |
| jsdom | 27.4.0 | MIT | `packages/flowzer-react` |
| openapi-typescript | 7.13.0 | MIT | `packages/flowzer-sdk`, `src/FlowzerConsole` |
| react | 19.2.8 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| react-dom | 19.2.8 | MIT | `packages/flowzer-react`, `src/FlowzerConsole` |
| sonner | 2.0.8 | MIT | `src/FlowzerConsole` |
| tailwind-merge | 3.6.0 | MIT | `src/FlowzerConsole` |
| tailwindcss | 4.3.3 | MIT | `src/FlowzerConsole` |
| typescript | 5.9.3 | Apache-2.0 | `packages/flowzer-react`, `packages/flowzer-sdk`, `src/FlowzerConsole` |
| typescript-eslint | 8.69.0 | MIT | `src/FlowzerConsole` |
| vite | 6.4.3 | MIT | `src/FlowzerConsole` |
| vitest | 5.0.0 | MIT | `packages/flowzer-react`, `packages/flowzer-sdk`, `src/FlowzerConsole` |
| zeebe-bpmn-moddle | 1.18.0 | MIT | `src/FlowzerConsole` |
| zustand | 5.0.15 | MIT | `src/FlowzerConsole` |

## Auffälligkeiten im Detail

- **bpmn-js** (npm, 18.28.0): Eigene "bpmn.io"-Lizenz (MIT-artig, im Paket als LICENSE hinterlegt) mit einer Zusatzbedingung: Das eingeblendete bpmn.io-Wasserzeichen im gerenderten Diagramm darf nicht entfernt oder verdeckt werden. Das ist keine Lizenzkollision, aber eine UI-Pflicht, die die Konsole einhalten muss.
- **FluentAssertions** (nuget, 8.9.0): Ab Version 8 lizenziert Xceed FluentAssertions unter der "Xceed Community License Agreement" (siehe Paket-LICENSE): kostenlos für Open-Source-Projekte und nicht-kommerzielle Nutzung, für kommerzielle Nutzung ist eine bezahlte Lizenz erforderlich. FluentAssertions wird hier ausschließlich in Testprojekten (nicht im ausgelieferten Produkt) verwendet; die genaue Einstufung als "nicht-kommerziell" für ein von einem Unternehmen betriebenes Open-Source-Projekt sollte trotzdem von den Maintainern bewusst getroffen werden.

Weiterer Kontext zur Meldung von Sicherheitslücken in einer dieser Abhängigkeiten oder im eigenen Code steht in [SECURITY.md](SECURITY.md).
