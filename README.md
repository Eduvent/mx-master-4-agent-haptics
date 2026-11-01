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

Copia `.codex/hooks.example.json` a `.codex/hooks.json` y revisa/confía los hooks
con `/hooks` en Codex CLI. Requiere `/usr/bin/python3`; la ruta del script se
resuelve desde Git, incluso si Codex inicia desde un subdirectorio.

Los hooks registran metadatos permitidos en `.local/events.jsonl`, sin guardar
mensajes, comandos ni argumentos. El plugin consulta nuevos registros cada
250 ms y emite `TestPulse` por cada `Stop` completo; ignora el historial al iniciar.
`Stop` significa fin del turno, no necesariamente éxito. `PermissionRequest`
puede ocurrir con aprobación automática y no demuestra atención del usuario.

```sh
python3 -m unittest discover -s tests -v
dotnet run --project tests/stop_reader/StopReaderTests.csproj -c Release
```

El observador experimental `hooks/observe_messages.py` lee solo mensajes nuevos
de un rollout local y guarda fecha, fase y presencia de interrogaciones sin texto.
No participa en la vibración y no clasifica de forma fiable la atención humana.
