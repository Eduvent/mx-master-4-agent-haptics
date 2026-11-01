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
