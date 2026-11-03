# Issue #1 — Configurable haptics
Objective: implement https://github.com/Eduvent/mx-master-4-agent-haptics/issues/1. Branch: feat/issue-1-configurable-haptics; base e84f38980ccba812c62cbd01f02178fb09b6f92d.
Delivery: feature-branch-chain (local work units only, no push/PR); forecast 800–1200 authored lines; final implementation 1931 authored lines across four cohesive slices, plus this tracking document. Runners: dotnet console tests, Python unittest, plugin build, software latency benchmark.

## Specs

### S1 — Accepted issue contract (verbatim)

> ## Objetivo
> 
> Permitir configurar qué señal háptica corresponde a cada estado de Codex, sin recompilar el hook ni editar sus comandos. Esta es una capa propia y reutilizable; detectar atención humana y producir los patrones físicos se implementarán por separado.
> 
> Actualmente Go escribe metadatos JSONL, `StopEventReader` solo cuenta registros Stop y el plugin C# siempre emite TestPulse, mapeado a `completed`.
> 
> ## Propuesta de arquitectura y MVP
> 
> - Mantener el hook Go como escritor ligero de metadatos, con salida `{}`, sin leer configuración por cada evento.
> - El plugin C# carga y valida una configuración local versionada, propuesta: `.local/haptics.json`, con una plantilla pública y documentada.
> - Separar eventos originales, eventos normalizados y perfiles hápticos. Empezar con `turn_finished`; reservar `approval_required` y `input_required` para fuentes validadas.
> - Cambiar el lector para devolver registros tipados con IDs de sesión/turno/solicitud disponibles; conservar lectura incremental, registros completos y ausencia de replay.
> - Permitir activar/desactivar globalmente y por evento, y seleccionar un perfil del catálogo soportado. En el MVP, cargar al iniciar/recargar el plugin; dejar explícito cuándo se aplican cambios.
> - Distinguir permisos de preguntas/decisiones que requieren una respuesta humana. Interrupciones y demás eventos permanecen sin alerta por defecto.
> - Sin configuración se conserva el comportamiento actual: Stop → perfil completed. Los perfiles nuevos y fuentes no verificadas permanecen desactivados.
> - Configuración inválida al iniciar: diagnosticar y usar valores predeterminados seguros. En una recarga fallida: conservar la última configuración válida. No bloquear Codex.
> - Un perfil describe una salida verificada del backend, no una promesa arbitraria de controlar el motor.
> 
> ## Criterios de aceptación
> 
> - [ ] Esquema, plantilla, valores predeterminados y límites documentados.
> - [ ] Cambiar una asignación a otro perfil soportado o desactivar una señal funciona al recargar el plugin, sin recompilar ni cambiar la definición de hooks.
> - [ ] Pruebas de archivo ausente/inválido, versión no admitida, valores/perfiles inválidos, desactivación y conservación del comportamiento Stop existente.
> - [ ] Pruebas de registros parciales, múltiples sesiones, duplicados e historial, preservando los identificadores necesarios.
> - [ ] Evitar confundir deduplicación con una espera global que descarte avisos de solicitudes distintas; definir política para eventos simultáneos.
> - [ ] El log conserva solo metadatos permitidos, sin mensajes ni argumentos.
> - [ ] Comparar latencia con el baseline Go actual y publicar metodología/resultados; sin volver a consultar Git ni introducir lectura de configuración en cada invocación Go.
> 
> ## Límites
> 
> Primera entrega por archivo, sin interfaz gráfica, sincronización en la nube ni ampliación del soporte de OS. macOS sigue siendo el sistema compatible; los perfiles cuentan con el catálogo real del backend.
> 
> ## Trabajo relacionado
> 
> - #2 — detectar aprobación humana y solicitudes de respuesta sin confundir permisos automáticos.
> - #3 — catálogo de perfiles, uno/dos/tres taps si el SDK lo permite, y verificación física.
> 
> Orden de entrega: esta base primero; investigación de señales (#2) y capacidades físicas (#3) pueden avanzar en paralelo. Después integrar los perfiles validados y habilitar atención solo con fuentes verificadas. No crear un TODO duplicado: los issues son el registro de trabajo.

### S2 — Authorized first delivery and limitation

> Sí, implementar #1 (recomendado)

The accepted question was: “¿Tomamos el issue #1 como la primera implementación, con ese alcance y dejando explícita la validación de perfiles alternativos pendiente de #3?”

### S3 — Delivery selection

> Cadena sobre rama de feature (recomendado)

## Tasks

- T1 | S1,S2 | two independent units: versioned configuration + typed incremental ingestion; each with tests/docs and its own local commit | delegated (parallel disjoint writers; context backstop) | done | commits bc6c00b + ead002b | Risk: item 4 (serialized watcher/timer callback boundary in ingestion unit)
- T2 | S1,S2 | plugin configuration routing and lifecycle integration, tests/docs | delegated (context backstop) | done | commit 4c2dccf | Risk: item 4 (lifecycle and synchronized callbacks)
- T3 | S1,S2 | full checks, privacy and latency comparison, independent verification | delegated (context backstop) | done | commit 4b657da | Risk: item 4 (lifecycle and synchronized callbacks)
- T4 | S1,S2,S3 | record verification/review outcomes, local work-unit boundaries and pending physical-profile proof; final handoff | inline | done | commit at feat/issue-1-closeout (docs: record issue 1 verification and delivery boundaries)

## Log

- L1 — Original request: “Ok implementemos el primer issue documento en nuestro proyecto o cual deberiamos elegir en cuanto a prioridad y dependencias?”
- L2 — User authorized issue #1 and explicitly left alternate-profile physical validation pending #3. Only completed is currently supported; no attention signals or arbitrary taps enabled.
- L3 — User chose feature-branch-chain. Planned local slices: configuration; typed ingestion; plugin integration + verification. No push, PR creation, installation, service restart, or physical pulse authorized by this implementation.
- L4 — Repository clean at base; origin/HEAD is origin/main. Inline gentle-ai CLI unavailable (exit 127); native review facade availability will be assessed without changing review mode.
- L5 — Implementation decisions: config applies on plugin load and an explicit reload action; no per-hook config reads. Global/per-event enabled with a completed-only catalog. Missing startup config uses defaults; invalid reload keeps last valid configuration. Reader compatibility adapter preserves existing count callers and synchronization.
- L6 — Configuration writer: completed code/tests/docs (648 authored lines); RED was compile failure for missing API, GREEN exit 0 with 10 groups/33 invalid cases using independent net8.0 runner. Initial net10.0 runner failed NETSDK1045, then its new runner target was corrected. Existing targets unchanged. Typed ingestion writer: partial, tests before and after implementation both exit 1 NETSDK1045 because installed SDK is 8.0.203 and existing project targets net10.0; no behavioral RED/GREEN claimed. No commits, integration, service changes or pulses.
- L7 — Native assessment: nativeRisk unassessable due undeclared untracked files; explicit item 4 escalation yields high and requires independent verification once runtime checks become available. No native review started. Latency project also needs new record compile link in T2/T3.
- L8 — Needs user decision was resolved: “Instalar .NET 10 local al proyecto (recomendado)”. Authorized official SDK download into .local/dotnet with local caches, no system SDK changes. Installer job-2 exited 0: SDK 10.0.403 installed in .local/dotnet, system SDK unchanged. T1 resumed with explicit local SDK; T2-T4 not started.
- L9 — Typed-ingestion checks now pass (exit 0; 9 new typed/compatibility/concurrency PASS lines plus existing reader/monitor suites), no implementation correction. Test isolation uses external temporary scratch as required. First SDK run printed development HTTPS certificate installation (no trust command); future commands set DOTNET_GENERATE_ASPNET_CERTIFICATE=false. No plugin/service actions occurred. Parent seam check job-3 exited 0 for both configuration and complete reader suites. T1 done; T2 started. Commits held until final independent concurrency verification; no publication.
- L10 — T2 completed: real behavioral RED exit 1 (gates, reload, diagnostics and lifecycle), GREEN exit 0 (10 plugin-double behavior groups), actual plugin Debug build exit 0 with zero warnings/errors against installed Logitech reference, complete reader suite exit 0. Reload action added without replacing reader/monitor; automatic routing completed-only and manual TestPulse remains independent. No hardware/service actions. Native assessment again unassessable until untracked scope declared; explicit concurrency escalation requires independent verification. T3 now covers full suite, latency and independent verifier.
- L11 — T3 writer full checks pass: Python 13 tests/zero skips, configuration 10 groups/33 invalid cases, reader suite, plugin 10 groups, actual SDK build zero warnings/errors, benchmark self-test. Isolated baseline/current 60 samples each: p50 49.0109/47.2425 ms, p95 57.7614/55.2107 ms; sequential blocks, no causal speedup claim. Results in tests/latency/results-configurable-haptics.json; Go unchanged. Parent assessment now includes intended staged files: native high, reviewDue true; explicit item4 independent verification required. Final native review will use work-unit commit ranges, not accumulated feature.
- L12 — Independent full verification passed all runners plus baseline parity, concurrency and burst probes, but found change-caused B1 (invalid UTF-16 escape in profile throws instead of startup defaults/last-valid reload diagnostic) and B2 (invalid UTF-16 optional metadata throws after consuming prior records, losing valid Stop). S1 AC3/AC4 unmet; T1 and T2 reopened; T3 pending targeted recheck. Correction allowed once for both blockers, with committed RED/GREEN regression cases and base/candidate reader comparison. Verifier evidence: /tmp/haptics-verifier.srfckR. No other advisories require correction; no commits/publication.
- L13 — B1/B2 correction observed behavioral RED configuration134/plugin1/reader1, then GREEN configuration11 groups/plugin12 groups/full reader suite and actual plugin build0. Optional malformed UTF16 fields become absent, preserving all three Stops. Base/candidate public count differential both3/0 with unchanged log hashes and empty stderr. Independent targeted recheck passed both original blockers and direct regressions; no blockers remain. Evidence /tmp/haptics-recheck.1qwoe0.
- L14 — Parent spot-check invocation omitted the documented DOTNET_CLI_HOME requirement, causing MSB4018/MSB3191 attempts at /plugin-tests on read-only root (exit1). No root directories were created. Diagnosed tests/plugin/PluginTests.csproj output property expansion; corrected invocation uses fresh external CLI_HOME/NuGet scratch, job-4 exited0 with all12 plugin groups passing. This was a runner invocation error, not a source defect or new verifier sweep. T1-T3 now verified; T4 closing local commits and native work-unit assessments.
- L15 — Slice 1: bc6c00bc08f122dc06d9d2f3c621ef175728631a (feat/issue-1-configuration), configuration parser/store + regressions/schema/example/docs, 704 authored lines. Cohesive pure API unit exceeds advisory400; no test/docs compression. Native committed-range assessment base e84f389: medium, reviewDue=false/under_budget, self-verification stands (independent feature verification already passed); no native START.
- L16 — Slice 2: ead002b4ad867e43e4c6cfe618b5e33db15035e0 (feat/issue-1-typed-events), typed ingestion + regressions/docs and compatibility latency compile link, 430 authored lines. Native medium with item4 escalation high; reviewDue=false/under_budget, independent concurrency/B2 verification already passed. No native START.
- L17 — Slice 3: 4c2dccfe7619558068d2ecb6a46c4cb6836ec924 (feat/issue-1-plugin), configuration lifecycle/router/reload action + SDK-double regressions/docs, 507 authored lines. Native medium with item4 escalation high; reviewDue=false/under_budget, independent lifecycle/B1 verification already passed. No native START.
- L18 — Slice 4: 4b657daf017a338ce369f4a04b129253696e8a39 (feat/issue-1-latency), real configuration-routing benchmark + self-test/help/provenance/results/docs, 296 authored lines. Native high due shell_process evidence string, reviewDue=true. User consent resolved by interactive host; native two-lens review readability/reliability approved. Exact acknowledgement closed review-a3efebc6674fffc1 and burned approved authority (native-approved-acknowledgement-completed). No source corrections from native review, no post-burn STATUS.
- L19 — Local chain boundaries: configuration -> typed-events -> plugin -> latency, integrated on feat/issue-1-configurable-haptics; each feat/issue-1-* branch pins its slice end. Tracking closeout is documentation only at feat/issue-1-closeout. Cohesive larger slices retain tests/docs rather than forced fragments. No push, PR, merge into main, GitHub issue closure, service restart, configuration installation or physical pulse.
- L20 — Final accepted scope satisfied with automated proof and actual SDK build. Physical loading/UI discovery/new-build vibration not exercised; alternate profiles/countable taps remain #3 and genuine attention sources remain #2, both intentionally disabled/unclaimed here. Next operational step requires separately authorized Logitech service reload + physical check. All initial SDK/runner failures and subsequent outcomes remain recorded above. Temporary isolated test evidence remains at the documented scratch paths for inspection; SDK10 remains ignored/local.
