# Cambios

## 0.1.0 — sin publicar

Primera versión.

- Detección de CD/DVD/BD (internos, USB e ISO montadas) mediante avisos de Windows, sin sondeo.
- Lectura y validación de `iberia-disc.json` (formato 1).
- Steam (bibliotecas adicionales), Epic Games, GOG (con o sin Galaxy), ejecutable local y ejecutable del disco.
- Aviso con carátula y cuenta atrás cancelable; pregunta siempre si el disco ya estaba dentro al arrancar o al volver de suspensión.
- Elección de instalación cuando hay varias, «Usar siempre esta opción», búsqueda automática acotada.
- Instalación por usuario sin administrador, inicio con Windows, acceso en el menú Inicio, entrada en Aplicaciones instaladas.
- Actualización abriendo el ejecutable nuevo; desinstalación limpia.
- Icono opcional en la bandeja, pausa, modo compatibilidad, modo portátil, prueba con carpeta.
- Mis juegos, prioridad de plataformas, carpetas de juegos, diagnóstico, logs, exportar/importar configuración.

### Seguridad (auditoría del 2026-10-07, ver docs/auditorias)

- Los programas de los discos se confirman cada vez, con el nombre del archivo, los argumentos y el aviso de origen no verificado; se comprueba que el disco no cambió antes de abrirlo.
- Nada se abre con la sesión bloqueada; el programa se niega a funcionar como administrador y solo carga DLL de System32.
- Textos y rutas del disco sin caracteres bidi ni invisibles; instaladores del disco sin argumentos; carátulas validadas por su cabecera.
- Las órdenes entre instancias solo van al programa instalado; importar una copia no trae ejecutables locales.
- GOG ya no lee `.info` de carpetas que cualquier cuenta puede crear; las rutas de red se ignoran.
- Esquema JSON alineado con el validador y comprobado en CI; workflow endurecido (acciones fijadas, atestación, compilación aparte).
