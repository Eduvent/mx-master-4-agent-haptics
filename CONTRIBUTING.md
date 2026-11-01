# Contribuir y validar ports

Los forks, issues y pull requests son bienvenidos. El sistema operativo compatible actualmente es **macOS**; la prueba realizada fue en Apple Silicon. Los ports a Windows/Linux pueden desarrollarse y compartirse como experimentales antes de terminar su validación.

Abrir un issue, experimentar en un fork o proponer un borrador no exige tener un mouse de prueba ni demostrar compatibilidad. Para incorporar un port como compatible en la documentación del proyecto sí se necesita la evidencia siguiente. El mantenedor revisa los resultados antes de cambiar esa declaración; una prueba positiva describe el entorno probado, no todas las versiones de un sistema.

## 1. Identificar el entorno

Incluye en el issue o pull request:

- Commit exacto probado (`git rev-parse HEAD`, ejecutado una vez para el informe, no dentro del hook).
- Sistema operativo, versión/build y arquitectura.
- Modelo del mouse (MX Master 4), versión de firmware si está disponible y conexión Bluetooth o receptor.
- Versiones del software/servicio Logitech o del componente que realmente envía la señal háptica.
- Versiones de Go, .NET y Python usadas en compilación, setup o pruebas.
- Instrucciones de instalación y cualquier paso manual necesario para arrancar el servicio.

Para Linux, explica qué componente reemplaza o proporciona la integración con el dispositivo: compilar el logger Go por sí solo no entrega la señal háptica. No se debe asumir que las dependencias de macOS existen en el sistema de destino.

## 2. Compilar y ejecutar las pruebas en el sistema de destino

Aporta comandos y resultados reproducibles desde un checkout limpio. Deben cubrir el contrato de eventos, privacidad del registro, errores sin bloquear Codex, escrituras concurrentes, rutas con espacios y directorios de trabajo distintos, además de detección de registros, duplicados y cierre del observador.

Usa las pruebas de `tests/` y añade o adapta las necesarias para el port. Actualiza los comandos de instalación y prueba si el shell, las rutas, el sufijo del ejecutable o el servicio son distintos. Una compilación cruzada, una máquina virtual sin acceso real al dispositivo o un CI sin mouse pueden validar partes del software; no sustituyen la prueba háptica en el sistema de destino.

El conjunto existente contiene referencias explícitas a `/usr/bin/python3`, `/bin/sh` y rutas macOS: no se considera portable sin revisar también esas pruebas y herramientas.

## 3. Probar el recorrido con hardware real

Con un MX Master 4 conectado al sistema de destino:

1. Prueba la acción manual del plugin y confirma que sientes su señal háptica.
2. Ejecuta el hook con un `Stop` sintético y comprueba registro, entrega al componente háptico y sensación física.
3. Completa al menos cinco turnos reales de Codex, separados y con el software Logitech activo. Anota los `Stop` esperados, las señales percibidas, los fallos y los posibles duplicados. La prueba debe incluir el recorrido automático, sin activar manualmente el evento para suplir al hook.
4. Reinicia el equipo y repite una prueba manual y un turno de Codex. Documenta si el servicio inicia solo o necesita una acción manual; no anuncies arranque automático si no se comprobó.
5. Confirma que los cambios no rompen las pruebas existentes de macOS. Si no tienes acceso a un Mac, indícalo y solicita esa verificación al mantenedor o a otro colaborador.

Si algún paso falla, conserva el resultado como un port experimental y describe la limitación. No ocultes intentos fallidos ni presentes solo una ejecución favorable como garantía general.

## 4. Qué demuestra que el mouse vibró

Un log de `Stop`, una respuesta `{}` o `event_raised` son evidencia de software. No miden el motor del mouse. Una grabación de pantalla tampoco permite confirmar por sí sola una sensación física.

La evidencia mínima práctica para revisar un port combina:

- Logs acotados y con fechas que permitan relacionar el evento con la entrega al dispositivo, sin errores relevantes.
- Una declaración explícita del probador de que sintió la señal, con el número de intentos y resultados.
- Pasos y entorno suficientes para que otra persona reproduzca la prueba.

Por ejemplo: «En el commit X, Windows versión Y y MX Master 4 por Bluetooth, probé cinco cierres reales de Codex: percibí cinco señales, sin señales adicionales. Después de reiniciar, fue necesario abrir el servicio manualmente».

Una grabación externa continua que muestre pantalla y mouse puede ayudar a revisar la secuencia. Si el sonido o el movimiento de la vibración se perciben en la grabación, indícalo; si no, no la presentes como una medición física. No es obligatorio comprar equipo ni subir un vídeo.

Para mayor confianza, es útil una reproducción independiente por otra persona. Si se necesita evidencia física objetiva, una medición sincronizada con un acelerómetro externo u otro sensor puede registrar la vibración; el proyecto no incluye actualmente esa instrumentación. Ninguno de estos medios debe describirse como prueba infalible ni como validación de sistemas que no se ensayaron.

## Plantilla breve para un informe de portabilidad

```text
Estado: experimental / solicita revisión de compatibilidad
Commit:
Sistema, versión/build y arquitectura:
Mouse, firmware disponible y conexión:
Software/servicio háptico y versiones:
Toolchains:
Pasos de instalación:
Comandos y resultados de pruebas automatizadas:
Prueba manual: intentos / señales percibidas / fallos
Cierres reales de Codex: intentos / señales percibidas / fallos o duplicados
Tras reiniciar: resultado y pasos manuales necesarios
Logs sintéticos o anonimizados:
Confirmación del probador de que sintió la vibración:
Grabación o reproducción independiente, si se dispone:
Limitaciones pendientes:
```

Usa eventos de prueba y anonimiza identificadores y rutas personales al compartir logs. No subas prompts, mensajes, comandos privados ni archivos completos de tu sesión. No hace falta mostrar tu rostro ni información personal en una grabación.
