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
```

Los benchmarks usan archivos temporales y no activan el mouse. Los hooks reciben `AGENT_HAPTICS_LOG` para aislar el registro. El ensayo C# usa pausas de 17–170 ms después de cada detección (semilla 42); el patrón afecta la distribución, especialmente con polling. El script Python mide hasta terminar el proceso y valida respuesta y registro; los campos `to_metadata` corresponden a la fecha preparada antes de escribir. Sus tiempos de CPU de hijos pueden omitir descendientes huérfanos como Git: se guardan solo como diagnóstico.

Los resultados completos están en:

- `tests/latency/results-2026-11-01.json`: polling, notificaciones y primer ensayo con Python.
- `tests/latency/results-hook-backends-2026-11-01.json`: eliminación de Git y comparación con Go. El campo de backend activo refleja el momento del experimento, antes de elegir Go como predeterminado.

Los ensayos con FSEvents requieren un entorno que permita las notificaciones nativas de macOS. El benchmark no modifica la confianza de hooks ni la configuración de Codex.
