# Configuración háptica versionada

El plugin carga y valida una configuración local al iniciar y mediante la acción **Recargar configuración háptica** del grupo **Codex** en Logitech. Solo controla señales automáticas; **Probar vibración** sigue emitiendo `TestPulse` independientemente de los interruptores de configuración.

## Archivo y valores predeterminados

La ubicación es `.local/haptics.json`, junto a `.local/events.jsonl`. El plugin resuelve ambos desde el directorio de compilación indicado por `CodexHapticsPlugin.link`, subiendo tres niveles hasta la raíz del repositorio; no depende del directorio de trabajo del servicio. Usa [haptics.example.json](haptics.example.json) como plantilla y [haptics.schema.json](haptics.schema.json) para validación editorial (JSON Schema 2020-12). El cargador recibe una ruta explícita, la convierte en absoluta una sola vez y no crea ni modifica archivos.

El documento mínimo válido es:

```json
{"schema_version": 1}
```

Los campos omitidos heredan estos valores:

| Campo | Valor | Efecto en el plugin |
| --- | --- | --- |
| `schema_version` | Obligatorio: entero `1` | No se aceptan otras versiones. |
| `enabled` | `true` | `false` silencia todas las señales automáticas sin borrar sus asignaciones. |
| `events.turn_finished.enabled` | `true` | Conserva Stop → `turn_finished` → `completed`. |
| `events.turn_finished.profile` | `"completed"` | Único perfil admitido actualmente. |
| `events.approval_required.enabled` | `false` | Nombre reservado, no se puede activar. |
| `events.input_required.enabled` | `false` | Nombre reservado, no se puede activar. |

`events` y cada objeto de evento son opcionales. Se permite un objeto vacío. Los eventos reservados no admiten `profile`; en el snapshot su perfil es `null`. El interruptor global no cambia los valores por evento: el consumidor debe comprobar ambos.

Para desactivar solo la señal de fin de turno:

```json
{
  "schema_version": 1,
  "events": {
    "turn_finished": {"enabled": false, "profile": "completed"}
  }
}
```

Para silenciar globalmente, conserva la plantilla y cambia el `enabled` de la raíz a `false`. Un perfil inválido se rechaza incluso si la señal está desactivada.

## Cuándo se aplican los cambios

El plugin construye el cargador al iniciar. Después solo la acción **Recargar configuración háptica** llama a `Reload()`; editar el archivo no cambia automáticamente el snapshot. No hace falta recompilar ni cambiar los hooks. La acción no reinicia el monitor ni su cursor y no repite historial. Un nuevo ciclo de descarga/carga también lee la configuración, pero ignora el historial al iniciar como antes. No hay watcher de configuración, sondeo ni lectura por invocación del hook Go; consultar `Snapshot` no hace E/S.

La acción aparece como `PluginDynamicCommand`, igual que **Probar vibración**, sin registrar otro evento háptico. El único evento de salida sigue registrado en `package/events/DefaultEventSource.yaml` y mapeado por `package/events/extra/eventMapping.yaml`: `completed` → `TestPulse`. No se habilitan perfiles alternativos ni atención.

Las lecturas, callbacks y disposición del monitor conservan su exclusión mutua. El plugin serializa recarga, selección del snapshot y despacho; un lote usa una sola configuración inmutable. Procesa los registros en orden, sin espera global ni deduplicación semántica de solicitudes distintas. Al descargar desactiva el despacho y espera al monitor **fuera** del lock de callbacks, evitando bloqueo mutuo; al terminar no quedan salidas automáticas pendientes.

| Situación | Estado publicado | Diagnóstico |
| --- | --- | --- |
| Inicio sin archivo o directorio padre | Valores predeterminados seguros | Ninguno (`null`). |
| Inicio con JSON inválido o versión/tipo/perfil no admitido | Valores predeterminados seguros | Motivo de validación. |
| Inicio con error de lectura | Valores predeterminados seguros | Error de lectura. |
| Recarga válida | Nuevo snapshot inmutable completo | Se borra (`null`). |
| Recarga sin archivo, inválida o ilegible | Misma referencia del último snapshot válido | Motivo del fallo. |

`Reload()` devuelve `true` únicamente cuando publica un documento válido. El cargador no escribe en stdout/stderr ni llama al SDK. El plugin registra el diagnóstico fijo en su log y añade `configuration_error: <motivo>` a `CodexHaptics.status`; los estados de inicio y despacho no sobrescriben un error de configuración pendiente. Una recarga válida lo borra. Los errores del observador y de carga usan `observer_error` y `load_error`, sin mensajes de excepción, rutas ni JSON privado. No propaga errores habituales de archivo ausente, E/S, acceso denegado o validación hacia los callbacks. Una ruta de programación nula, vacía o inválida sí puede provocar una excepción en el constructor.

Los diagnósticos no contienen el JSON recibido, valores desconocidos ni rutas de archivo. Ejemplos: `schema_version debe ser el entero 1.`, `events.turn_finished.profile solo admite completed.` y `No se encontró la configuración al recargar; se conserva la última válida.`

## API pública para integración

Namespace: `Loupedeck.CodexHapticsPlugin`.

- `HapticsConfiguration.Default`: snapshot predeterminado compartido e inmutable.
- `HapticsConfiguration.TryParse(string json, out HapticsConfiguration configuration, out string diagnostic)`: `true` con snapshot y diagnóstico `null`; `false` con configuración `null` y diagnóstico. No lee archivos.
- `HapticsConfiguration`: propiedades de solo lectura `int SchemaVersion`, `bool Enabled`, `HapticsEventConfiguration TurnFinished`, `ApprovalRequired`, `InputRequired`.
- `HapticsEventConfiguration`: propiedades de solo lectura `bool Enabled`, `string Profile` (puede ser `null` para nombres reservados).
- `new HapticsConfigurationStore(string path)`: carga inicial, sin servicios ni dependencias del SDK.
- `HapticsConfigurationStore.Snapshot`: configuración actual; conservar esta referencia durante cada decisión de despacho.
- `HapticsConfigurationStore.Diagnostic`: último diagnóstico o `null`.
- `HapticsConfigurationStore.Reload()`: recarga explícita; devuelve `bool`.
- `CodexHapticsPlugin.ReloadHapticsConfiguration()`: punto de entrada de la acción Logitech; devuelve `false` si el plugin no está activo o la recarga falla. No reconstruye el monitor.

Ejemplo del contrato puro (el plugin utiliza además el router de catálogo):

```csharp
var store = new HapticsConfigurationStore(configurationPath);
// Al recibir la acción explícita de recarga:
var applied = store.Reload();
var diagnostic = store.Diagnostic;
// En el callback de un evento normalizado turn_finished:
var snapshot = store.Snapshot;
if (snapshot.Enabled && snapshot.TurnFinished.Enabled)
{
    var profile = snapshot.TurnFinished.Profile; // completed
    // El router del plugin traduce completed a TestPulse, fuera del cargador.
}
```

Las recargas se serializan con un lock y publican atómicamente una referencia inmutable; los lectores no necesitan ese lock. Las referencias anteriores siguen siendo válidas. `Snapshot` y `Diagnostic` son lecturas independientes del estado: si se consultan por separado durante una recarga concurrente, pueden corresponder a publicaciones distintas. Esta API no modifica la sincronización del monitor.

## Límites de esta entrega

- Nombres, mayúsculas, tipos y propiedades son estrictos. Se rechazan propiedades desconocidas y duplicadas en cualquier objeto, `null`, comentarios y comas finales. Los campos opcionales omitidos no equivalen a `null`.
- La versión debe escribirse como entero `1`, no como cadena, decimal o exponente. JSON Schema considera `1.0` matemáticamente entero; el parser exige además la representación entera. Los duplicados también requieren validación del parser, no solo del esquema.
- Solo `completed` representa una salida ya verificada del backend. No se aceptan taps arbitrarios ni perfiles alternativos, ni siquiera con `enabled: false`. Su validación física queda pendiente de #3.
- Los nombres de atención no habilitan fuentes nuevas: distinguir aprobación, permisos automáticos y preguntas sigue pendiente de #2. Interrupciones y eventos desconocidos no reciben asignaciones en este modelo.
- No hay editor gráfico de configuración, nube, ampliación de sistemas operativos ni cambio en los hooks. macOS sigue siendo el sistema compatible del plugin; las clases puras no dependen de él.

## Verificación aislada

```sh
dotnet run --project tests/configuration/ConfigurationTests.csproj -c Release
```

El ejecutable de configuración usa .NET 8, sin paquetes externos, y enlaza solo las dos clases puras. Con un SDK/runtime exclusivamente .NET 10, ejecútalo con `DOTNET_ROLL_FORWARD=Major` explícito. No requiere construir el plugin (.NET 10), instalar servicios ni emitir pulsos. Comprueba el contrato público, valores predeterminados, corpus de errores, archivos ausentes/ilegibles, conservación de la última configuración válida, recarga explícita, concurrencia y coherencia básica del esquema/plantilla. El pequeño evaluador del esquema cubre sus palabras clave utilizadas; no sustituye un validador JSON Schema completo.

La integración se prueba por separado con .NET 10 y dobles del SDK, sin servicio real ni pulsos:

```sh
dotnet run --project tests/plugin/PluginTests.csproj -c Release
```

Asigna `DOTNET_CLI_HOME` y `NUGET_PACKAGES` a un directorio temporal externo para este runner; sus artefactos se generan bajo `DOTNET_CLI_HOME/plugin-tests`. Las pruebas llaman a `Load`, `Unload` y las acciones públicas; cubren interruptores, recarga, ausencia de replay, fuentes reservadas, errores y concurrencia con despacho bloqueado. No prueban que el servicio haya cargado el plugin ni la vibración física.

Los archivos de prueba viven en directorios temporales únicos y se eliminan al terminar. Los artefactos de compilación se redirigen fuera del repositorio, a `codex-haptics-configuration-tests` bajo el directorio temporal del sistema.
