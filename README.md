# MX Master 4 Agent Haptics

Plugin local de Logi Plugin Service para probar vibraciones del MX Master 4 en macOS.
La acción **Probar vibración** emite `TestPulse`, mapeado al patrón `completed`.

Requiere .NET 10 SDK y `PluginApi.dll` de la instalación local de Logi Plugin Service.
No se redistribuye el SDK de Logitech.

```sh
dotnet build CodexHapticsPlugin/src/CodexHapticsPlugin.csproj -c Debug
```

Para cargarlo, crea un enlace de desarrollo `CodexHapticsPlugin.link` en
`~/Library/Application Support/Logi/LogiPluginService/Plugins/` que contenga la ruta
absoluta a `CodexHapticsPlugin/bin/Debug/` de este repositorio. Reinicia Logi Plugin
Service y prueba la acción manual. La compilación normal no instala ni recarga el plugin.

## Eventos de Codex y Python

Ejecuta `python3 hooks/configure_hooks.py` para generar `.codex/hooks.json` y revisa/confía los hooks
con `/hooks` en Codex CLI. Requiere `/usr/bin/python3`; la ruta absoluta del script se
resuelve al configurar, sin ejecutar Git en cada evento ni depender del directorio actual.

Los hooks registran metadatos permitidos en `.local/events.jsonl`, sin guardar
mensajes, comandos ni argumentos. El plugin detecta cambios de archivo, con una lectura de respaldo cada segundo y emite `TestPulse` por cada `Stop` completo; ignora el historial al iniciar.
`Stop` significa fin del turno, no necesariamente éxito. `PermissionRequest`
puede ocurrir con aprobación automática y no demuestra atención del usuario.

```sh
python3 -m unittest discover -s tests -v
dotnet run --project tests/stop_reader/StopReaderTests.csproj -c Release
```

El observador experimental `hooks/observe_messages.py` lee solo mensajes nuevos
de un rollout local y guarda fecha, fase y presencia de interrogaciones sin texto.
No participa en la vibración y no clasifica de forma fiable la atención humana.

## Medición de detección

Se compararon 60 muestras por variante: polling 250 ms (p50 169,8 ms), polling
25 ms (12,6 ms) y notificaciones (12,3 ms). Se eligieron notificaciones más respaldo.
Los resultados completos están en `tests/latency/results-2026-11-01.json`.

```sh
dotnet run --project tests/latency/Latency.csproj -c Release -- 60
dotnet run --project tests/latency/Latency.csproj -c Release -- 60 hook250 hookwatcher
```

Usan logs temporales, sin vibraciones; las pruebas nativas de FSEvents requieren
un entorno sin restricciones sobre notificaciones. Los tiempos terminan antes del
transporte Logitech y la vibración física. Las ventanas cortas de CPU son orientativas.

## Resolver las rutas una vez

El configurador conserva otros handlers/timeouts, usa quoting de shell y no
reescribe definiciones sin cambios. Repite la configuración si mueves el repo y
vuelve a confiar en los hooks modificados. El archivo generado con rutas personales
está ignorado; se versiona la plantilla portable.
