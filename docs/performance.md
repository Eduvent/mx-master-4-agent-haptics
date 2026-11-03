# Medición de latencia

Mediciones locales del 1 de noviembre de 2026, macOS arm64, Python del sistema, Go 1.27.1 y .NET 10. No representan una garantía para otros equipos.

## Del polling a las notificaciones

Con 60 muestras por variante:

| Escritura → detección | p50 (ms) | p95 (ms) | Lecturas periódicas en 8 s de reposo |
| --- | ---: | ---: | ---: |
| Polling de 250 ms | 169,8 | 226,1 | 32 |
| Polling de 25 ms | 12,6 | 25,6 | 320 |
| Notificaciones sin respaldo | 12,3 | 12,9 | 0 |

Se eligieron notificaciones con una lectura de respaldo cada segundo. Las consultas periódicas bajaron de cuatro a una por segundo; el respaldo recupera eventos si falla una notificación. En el primer ensayo con shell + Python + observador, la mediana bajó de 171,0 a 66,0 ms, con p95 de 226,4 a 84,6 ms.

En ventanas de reposo de ocho segundos del ensayo completo se observaron 119,7 ms de CPU con polling y 8,6 ms con el observador elegido; las asignaciones administradas fueron 146.528 y 36.224 bytes. Las ventanas son cortas y contienen ruido de runtime/JIT: no estiman batería o consumo sostenido. La memoria residente se acumula porque las variantes comparten proceso y no permite aislar su coste.

## Resolver las rutas una vez y migrar a Go

Primero se compararon 120 muestras por variante: quitar `git rev-parse` redujo la mediana del comando de 36,8 a 28,5 ms, unos 8,3 ms (23 %). Después se repitió incluyendo Go, con cinco calentamientos por variante y 120 muestras alternadas en orden aleatorio reproducible (semilla 42):

| Shell → salida del proceso | Mediana (ms) | p95 (ms) |
| --- | ---: | ---: |
| Python + Git | 37,0 | 41,8 |
| Python con ruta fija | 28,6 | 32,4 |
| Go con ruta fija | 6,7 | 7,5 |

El shell está incluido en todas las variantes. Go ahorró unos 22 ms adicionales frente a Python sin Git, aproximadamente un 77 %. Un ensayo anterior había medido 18,6 ms para arrancar Python sin trabajo: ejecutar el hook nativo evita ese coste del intérprete y sus importaciones.

Con 60 muestras por bloque y el mismo lanzador .NET y observador:

| Shell → callback del observador | p50 (ms) | p95 (ms) |
| --- | ---: | ---: |
| Python + Git | 69,7 | 82,4 |
| Python con ruta fija | 59,9 | 73,7 |
| Go con ruta fija | 37,2 | 46,7 |

Este ensayo incluye el coste de `.NET Process.Start` del banco de pruebas; sus valores absolutos no equivalen al despacho interno de Codex. Excluye transporte Logitech y vibración física. No se suman medianas de ensayos separados.

Se verificó además el plugin cargado, ejecutando desde `/tmp` un `Stop` sintético por backend con su ruta predeterminada del log: el evento `event_raised` apareció unos 39,1 ms después del lanzamiento con Python y 9,4 ms con Go. Son comprobaciones únicas de integración, no distribuciones ni confirmaciones sensoriales.

## Configuración: baseline Go frente al despacho tipado

Ensayo aislado registrado el **2 de noviembre de 2026** en macOS 27.0.1
(26A434), arm64, Go 1.27.1 y SDK .NET 10.0.403. No reutiliza las cifras
históricas anteriores como baseline del cambio de configuración.

| Lanzamiento del shell → callback de selección | Muestras | p50 (ms) | p95 (ms) | Máximo (ms) |
| --- | ---: | ---: | ---: | ---: |
| Baseline Go + lector/monitor por conteo, commit `e84f389` | 60 | 49,0109 | 57,7614 | 198,9351 |
| Mismo Go + registros tipados + configuración + router real | 60 | 47,2425 | 55,2107 | 58,2136 |

El baseline se ejecutó desde un `git archive` externo del commit completo
`e84f38980ccba812c62cbd01f02178fb09b6f92d`, sin cambiar su fuente: su modo
`hook_go_static` usa el lector/monitor antiguo por conteo. **No** es el adaptador
de compatibilidad actual. El segundo recorrido usa la fuente de trabajo,
`StopEventMonitor.ForRecords`, `HapticsConfigurationStore.Snapshot` y
`HapticsEventRouter.BackendEvent`; termina cuando selecciona `TestPulse`, sin
llamar al SDK. El SHA-256 de `hooks/go/main.go` coincide en ambos árboles
(`5dc02e40…53bc31d`). Se compiló Go una sola vez en el árbol baseline temporal y
se copió el mismo ejecutable al árbol actual temporal; ambos hashes del binario
coinciden. Los hashes completos y las salidas están en el artefacto.

La configuración real `schema_version: 1`, globalmente habilitada, con
`turn_finished` habilitado y perfil `completed`, se escribió en un temporal y
se analizó **una sola vez antes de crear el monitor y del bucle de muestras**.
El callback obtiene el snapshot por lote; no recarga archivos. Go sigue sin
leer configuración ni ejecutar Git por evento.

Se cronometró desde justo antes de `.NET Process.Start` (`/bin/sh -c` con ruta
absoluta de Go) hasta el callback antiguo o la selección del evento backend.
Incluye arranque del lanzador/shell/Go, append, notificaciones nativas y lector
C#; excluye compilación, carga inicial de configuración, salida posterior del
proceso, despacho real de Logitech y vibración. El proceso se espera y su código
de salida se comprueba después del intervalo medido. Ambos monitores usan
notificaciones con reconciliación de 1000 ms.

Orden: un bloque baseline, luego uno configurado, en procesos independientes;
no alternado ni contrabalanceado. Cada bloque tiene 200 ms de asentamiento y
8 s de reposo; no se descartaron eventos de calentamiento. Las pausas entre
muestras son 17–170 ms con `Random(42)`, con un único evento pendiente. Se
conservaron todos los valores, incluido el máximo baseline de 198,9351 ms: no
se instrumentó su causa. El campo histórico `median_ms` del runner C# es el
valor central inferior (posición 30 de 60); p95 es rango más cercano (57 de 60).

**Este ensayo no muestra un aumento de p50/p95, pero no demuestra una mejora ni
aísla el coste de configurar.** El equipo no estaba dedicado al benchmark;
arranque, JIT, planificación y filesystem pueden dominar. No mide recarga,
carga concurrente, llegada interna de eventos Codex ni movimiento físico.
No deben restarse o sumarse medianas de límites distintos.

Como diagnóstico separado, `benchmark_hook.py --samples 60 --modes go_static`
registró mediana **8,859667 ms**, p95 **10,177375 ms** y máximo **12,07875 ms**
hasta salida del proceso, con cinco calentamientos excluidos. Usó Python 3.11.5
como lanzador y el mismo binario Go; con un único modo no hay alternancia real,
aunque el script conserve su etiqueta genérica de orden. No es una segunda
comparación de routers ni una medición háptica.

Todos los eventos mutables y configuraciones estuvieron en temporales externos,
con `AGENT_HAPTICS_LOG` explícito para los hooks. No se escribió en
`.local/events.jsonl`, no se instaló/recargó un plugin y no se tocó el servicio
ni el mouse. Las suites Python (13 pruebas, ninguna omitida), configuración
(10 grupos/33 casos inválidos), lector/monitor y dobles del plugin pasaron;
el build Debug real, en una copia temporal con la referencia Logitech instalada,
terminó sin warnings ni errores. Estas comprobaciones no validan físicamente
esta compilación ni perfiles alternativos (pendientes de #3).

## Repetir las pruebas

Desde la raíz del repositorio y con Go compilado como indica el README:

```sh
# Solo escritura/detección: 60 eventos por variante.
dotnet run --project tests/latency/Latency.csproj -c Release -- 60

# Paso 1: Python con/sin Git.
python3 tests/benchmark_hook.py --modes legacy_python python_static

# Paso 2: incluye Go; 120 muestras por variante.
python3 tests/benchmark_hook.py

# Recorrido hasta el callback del observador.
dotnet run --project tests/latency/Latency.csproj -c Release -- \
  60 hookwatcher hook_python_static hook_go_static

# Autocomprobación determinista de la nueva ruta, sin tiempos ni SDK.
dotnet run --project tests/latency/Latency.csproj -c Release -- --self-test

# Configuración real de startup → selección de TestPulse (sin SDK).
dotnet run --project tests/latency/Latency.csproj -c Release -- 60 hook_go_configured
```

Para repetir la comparación nueva, prepara dos copias externas: el baseline con
`git archive e84f38980ccba812c62cbd01f02178fb09b6f92d` y una copia del código
actual que incluya los archivos nuevos de configuración/router. Compila Go en
`.local/bin/record-event` del baseline temporal y copia ese mismo binario al
mismo lugar de la copia actual. Ejecuta allí, en ese orden:

```sh
# CWD: baseline temporal; fuente del commit sin modificar.
dotnet run --project tests/latency/Latency.csproj -c Release -- 60 hook_go_static
# CWD: copia actual temporal.
dotnet run --project tests/latency/Latency.csproj -c Release -- 60 hook_go_configured
python3 tests/benchmark_hook.py --samples 60 --modes go_static
```

Usa el mismo SDK 10 mediante `PATH` en ambas copias y asigna `DOTNET_CLI_HOME`,
`NUGET_PACKAGES` y las cachés de Go a temporales externos, nunca al checkout.
Para evitar preparación de certificados y telemetría, exporta
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, `DOTNET_NOLOGO=1`,
`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` y `DOTNET_CLI_TELEMETRY_OPTOUT=1`.
`--help` describe los modos; los modos anteriores y su formato JSON se conservan.
`--self-test` verifica snapshot de startup aun tras invalidar el archivo,
ambos interruptores, selección `TestPulse`, omisión de atención/historial,
ausencia de replay y consumo por conteo previo. El RED observado fue de
compilación por falta del helper nuevo; las distribuciones temporales no tienen
un RED determinista significativo.

Los benchmarks usan archivos temporales y no activan el mouse. Los hooks reciben `AGENT_HAPTICS_LOG` para aislar el registro. El ensayo C# usa pausas de 17–170 ms después de cada detección (semilla 42); el patrón afecta la distribución, especialmente con polling. El script Python mide hasta terminar el proceso y valida respuesta y registro; los campos `to_metadata` corresponden a la fecha preparada antes de escribir. Sus tiempos de CPU de hijos pueden omitir descendientes huérfanos como Git: se guardan solo como diagnóstico.

Los resultados completos están en:

- `tests/latency/results-configurable-haptics.json`: comparación aislada del baseline real frente a configuración, comandos, hashes, entorno y límites; nombre sin fecha inferida.

- `tests/latency/results-2026-11-01.json`: polling, notificaciones y primer ensayo con Python.
- `tests/latency/results-hook-backends-2026-11-01.json`: eliminación de Git y comparación con Go. El campo de backend activo refleja el momento del experimento, antes de elegir Go como predeterminado.

Los ensayos con FSEvents requieren un entorno que permita las notificaciones nativas de macOS. El benchmark no modifica la confianza de hooks ni la configuración de Codex.
