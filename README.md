# MX Master 4 Agent Haptics

Vibración del Logitech MX Master 4 al terminar un turno de Codex. El hook Go registra metadatos locales; un plugin C# de Logi Plugin Service detecta nuevos eventos `Stop` y emite `TestPulse` con el patrón háptico `completed`.

Verificado en macOS con MX Master 4 y Logi Plugin Service 6.4.2. `Stop` significa que terminó el turno; no garantiza que la tarea haya tenido éxito. El mouse solo recibe señales mientras el servicio y el plugin estén activos.

## Requisitos

- MX Master 4, Logi Options+ y Logi Plugin Service con soporte de plugins hápticos.
- .NET 10 SDK para compilar el plugin. La referencia `PluginApi.dll` se toma de la instalación local de Logi Plugin Service; no se redistribuye aquí.
- Go 1.22 o posterior para compilar el hook una vez; el evento ejecuta el binario, sin compilador.
- Python 3.8 o posterior para configurar las rutas y ejecutar las pruebas. El hook Python se conserva como alternativa y referencia de comparación.
- Codex con soporte de hooks locales y el proyecto abierto como directorio de trabajo.

La integración del plugin y sus rutas de instalación están preparadas para macOS; Windows no se ha validado.

## Instalación local de desarrollo

Ejecuta desde la raíz del repositorio:

```sh
# 1. Compilar el hook Go.
mkdir -p .local/bin
GOTOOLCHAIN=local GOPROXY=off go build -trimpath -o .local/bin/record-event ./hooks/go/main.go

# 2. Generar comandos con rutas absolutas, resueltas una sola vez.
python3 hooks/configure_hooks.py

# 3. Compilar el plugin.
dotnet build CodexHapticsPlugin/src/CodexHapticsPlugin.csproj -c Debug

# 4. Registrar el enlace local de desarrollo.
mkdir -p "$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
printf '%s\n' "$PWD/CodexHapticsPlugin/bin/Debug/" > \
  "$HOME/Library/Application Support/Logi/LogiPluginService/Plugins/CodexHapticsPlugin.link"
```

Reinicia Logi Plugin Service para cargar el plugin y prueba su acción **Probar vibración**. Después revisa y confía en los hooks de Codex; en la CLI se hace con `/hooks`. Codex exige volver a confiar cuando cambia la definición de un comando. Según la versión de la app, puede ser necesario abrir un chat nuevo para cargar la configuración. Referencia: [hooks oficiales de Codex](https://learn.chatgpt.com/docs/hooks).

El configurador crea `.codex/hooks.json` a partir de `.codex/hooks.example.json`, conserva otros handlers y timeouts y no reescribe una definición sin cambios. La configuración generada contiene rutas de tu máquina y no se versiona. Si mueves el repositorio, repite la configuración y actualiza el archivo `.link`. Si borras `.local`, recompila el binario Go.

Para seleccionar explícitamente un backend:

```sh
python3 hooks/configure_hooks.py --backend go
python3 hooks/configure_hooks.py --backend python
```

La alternativa Python usa `/usr/bin/python3` en esta instalación macOS. Ambas opciones evitan ejecutar `git rev-parse` por evento y funcionan desde subdirectorios del proyecto.

## Funcionamiento y privacidad

El registro predeterminado es `.local/events.jsonl`. Go lo resuelve respecto a `.local/bin/record-event`, Python respecto al script; `AGENT_HAPTICS_LOG` permite cambiarlo en pruebas. Se registra fecha UTC, evento e identificadores/metadatos permitidos. No se guardan prompts, mensajes, comandos ni argumentos de herramientas. Para `Stop`, el diagnóstico puede incluir un booleano que indica si el último mensaje tiene signos de interrogación; no es un detector fiable de atención humana.

Los hooks registran `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PermissionRequest`, `Stop` e `Interrupt`. Solo `Stop` activa la vibración automática. `PermissionRequest` también puede ocurrir con aprobación automática: por sí solo no demuestra que el usuario deba intervenir.

El plugin usa notificaciones de cambios de archivo y una lectura de respaldo cada segundo. Serializa las lecturas para evitar duplicados, espera registros completos e ignora el historial al iniciar. Si las notificaciones fallan, el respaldo puede añadir aproximadamente un segundo de espera. El escritor usa un log de append; reemplazar o rotar el archivo por otro del mismo tamaño o mayor no está soportado de forma fiable por el lector actual.

Para diagnosticar:

```sh
tail -f .local/events.jsonl
cat "$HOME/Library/Application Support/Logi/LogiPluginService/CodexHaptics.status"
```

El estado esperado al cargar es `watching_notifications_with_1000ms_reconciliation`; después de un evento aparece `event_raised`. Si falla la carga, verifica el `.link`, la ubicación del SDK, el servicio activo y la confianza de los hooks. La compilación normal no instala ni recarga el plugin. El enlace es de desarrollo: el lector depende de la estructura `CodexHapticsPlugin/bin/Debug/` de este repositorio.

## Pruebas y rendimiento

Con el binario Go compilado:

```sh
python3 -m unittest discover -s tests -v
dotnet run --project tests/stop_reader/StopReaderTests.csproj -c Release
```

Se cubren el contrato de ambos hooks, omisión de contenido privado, entradas inválidas, errores de escritura, concurrencia, independencia del directorio de trabajo, quoting, configuración idempotente, deduplicación y cierre del observador. Las pruebas C# con notificaciones nativas deben ejecutarse fuera de un sandbox que bloquee FSEvents de macOS; dentro se observó `Failed to start the EventStream`.

En 120 muestras alternadas por variante, el comando completo tuvo medianas de **37,0 ms** con Python + Git, **28,6 ms** con Python sin Git y **6,7 ms** con Go sin Git. El salto a Go redujo aproximadamente un 77 % frente a Python sin Git. Son mediciones de software de este equipo, no del movimiento físico del mouse.

[Metodología, límites, resultados y benchmarks reproducibles](docs/performance.md). Los logs, cachés y binarios locales quedan fuera de Git; se conservan las pruebas y los resultados de comparación útiles para repetir el experimento.

## Diagnóstico experimental de mensajes

`hooks/observe_messages.py RUTA_AL_ROLLOUT .local/message-observation.jsonl --seconds 90` observa únicamente mensajes nuevos de un rollout local y guarda fecha, fase y presencia de signos de interrogación. No guarda el texto y no participa en la integración háptica. El formato de rollout es interno e inestable; este script es un diagnóstico temporal, no un detector de preguntas que requieran respuesta.
