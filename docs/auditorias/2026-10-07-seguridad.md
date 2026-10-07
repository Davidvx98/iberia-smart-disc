# Auditoría de seguridad — 2026-10-07

Tres revisiones independientes, solo estáticas:

| Prefijo | Área |
|---|---|
| DISCO | Del disco a la ejecución: manifiesto, rutas, confianza, lanzamiento, carátulas. |
| WIN | Integración con Windows: instalación, actualización, desinstalación, órdenes entre procesos, registro, configuración. |
| PUB | Publicación: repositorio público, GitHub Actions, esquema, contrato con la web. |

Ninguna se ejecutó en Windows real. Las correcciones tienen tests de regresión
en `tests/IberiaSmartDisc.Core.Tests/SecurityRegressionTests.cs` (uno por
hallazgo, con el ID en el nombre) y en `ManifestCorpusTests.cs`. Los hallazgos
repetidos entre revisiones se agrupan.

## Resumen

43 hallazgos (11 DISCO, 17 WIN y 15 PUB), agrupados en 38 entradas porque
algunos se repiten entre revisiones:

- **Corregidas:** 31.
- **Mitigadas:** 4, con un riesgo residual explicado.
- **Pendientes del propietario o de la web:** 3 (firma, licencia y contrato con
  la web), más dos ajustes del repositorio en PUB-06.

## Hallazgos

| ID | Gravedad | Hallazgo | Estado |
|---|---|---|---|
| DISCO-01 · PUB-03 · WIN-05 | Alta | La confianza en el programa de un data-disc dependía solo del hash del JSON. Un disco o ISO con el mismo manifiesto y otro `.exe` se abría sin preguntar. | **Corregido.** Se pregunta cada vez y no se guarda confianza. |
| DISCO-02 | Media | Se procesaban discos con la sesión bloqueada. | **Corregido.** Con la sesión bloqueada no se abre nada; al desbloquear, pregunta. |
| DISCO-03 | Media | Cambiar el disco con un diálogo abierto ejecutaba el del disco nuevo. | **Corregido.** Antes de abrir se comprueban el número de serie del volumen, el hash del manifiesto y la expulsión. |
| DISCO-04 | Media | Caracteres bidi o invisibles en nombres y rutas podían engañar en el diálogo de confianza. | **Corregido.** Se rechazan en el programa y en el esquema (`TextSafety`). |
| WIN-01 | Media | Al abrirlo desde Descargas podía cargar una DLL de esa carpeta. | **Mitigado.** `SetDefaultDllDirectories` y `DefaultDllImportSearchPaths` limitan la búsqueda a System32. Residual: lo que se carga antes de `Main` solo se cubre firmando. |
| WIN-02 | Media | Podía quedarse residente con permisos de administrador. | **Corregido.** Se niega a funcionar elevado con el UAC activo. |
| WIN-03 | Media | Importar una copia podía dejar ejecutables, argumentos y confianza que se abrían solos. | **Corregido.** No se importan ejecutables locales; la confianza y los argumentos ya no existen. |
| WIN-04 | Media | Un `goggame-*.info` en `C:\GOG Games` (que cualquier cuenta puede crear) abría su programa. Las bibliotecas UNC provocaban conexiones SMB. | **Corregido.** Solo se leen en carpetas del usuario y en `Program Files (x86)\GOG Galaxy\Games`. Las rutas de red se ignoran. |
| PUB-01 | Media | El esquema aceptaba 28 manifiestos que el programa rechaza. | **Corregido.** Esquema reescrito y corpus compartido en CI (`tools/check-schema.py`). |
| PUB-02 | Media | Ejecutable sin firmar; el README enseñaba a saltarse SmartScreen; Smart App Control lo bloquea. | **Mitigado en la documentación.** Pendiente: firmar. |
| PUB-04 | Media | Sin licencia, sin `SECURITY.md` y sin un canal de aviso concreto. | `SECURITY.md` añadido. Pendiente: elegir la licencia y activar el aviso privado de vulnerabilidades. |
| PUB-05 | Media | Requisitos para quien genere el manifiesto: longitudes, datos privados en `discId`, datos personales e inyección de campos. | **Documentado** en FORMATO-DISCO §6. Pendiente de implementar en la tienda. |
| DISCO-05 · WIN-09 | Baja | El diálogo de confianza confirmaba con Intro y tomaba el foco. | **Corregido.** Cancelar es el botón por defecto y el de confirmar se activa a los 1,5 s. |
| DISCO-06 | Baja | `arguments` no se mostraba; en `.msi` podía cambiar la instalación. | **Corregido.** Se muestra; los instaladores no admiten argumentos. |
| DISCO-07 | Baja | La carátula se decodificaba con GDI+ sin comprobar el tipo real (EMF/WMF) ni el tamaño. | **Corregido.** Cabecera PNG/JPEG/BMP, coincidencia con la extensión, 16 megapíxeles como máximo y sin metarchivos. Residual: fallos nativos de GDI+ con imágenes válidas. |
| DISCO-08 | Baja | Un disco puede usar el `game.id` de otro juego y heredar su preferencia. | **Mitigado.** El aviso muestra el nombre que da la plataforma. Aceptado: el `game.id` no está firmado (v0.3). |
| DISCO-09 | Baja | Las pistas del disco se saltaban el filtro de desinstaladores y el resultado quedaba marcado. | **Corregido.** |
| DISCO-10 · PUB-10 | Baja | Se admitían `con`, `nul`, `com1`… como `game.id`, y un salto de línea final en los identificadores. | **Corregido.** |
| DISCO-11 | Info | Los errores repetían texto del disco. | **Corregido.** |
| WIN-06 | Baja | Actualizar reactivaba el inicio desactivado en el Administrador de tareas. | **Corregido.** Solo lo reactiva una decisión expresa del usuario. |
| WIN-07 | Baja | Las órdenes se enviaban a cualquier ventana con el título esperado. | **Corregido.** Se comprueba el ejecutable del proceso dueño. |
| WIN-08 | Baja | Ocupar el mutex bloqueaba actualizar o desinstalar; el auxiliar de desinstalación lo podía lanzar cualquiera. | **Corregido.** Se espera al proceso real; el auxiliar comprueba que su padre es el programa instalado. |
| WIN-10 | Baja | `--portable --uninstall` borraba los datos equivocados. | **Corregido.** La combinación se rechaza. |
| WIN-11 | Baja | El auxiliar se rendía a los 40 s y dejaba restos en `%TEMP%`. | **Corregido.** Espera sin límite y la siguiente instalación limpia los restos. Aceptado: hasta entonces queda el `.exe` apartado en `%TEMP%`. |
| WIN-12 | Baja | Se abría `explorer.exe` sin ruta completa. | **Corregido.** |
| WIN-13 | Info | No se comprobaba el tamaño de `DEV_BROADCAST_VOLUME`. | **Corregido.** |
| WIN-14 | Info | `test-disc` de otro proceso mostraba su texto y admitía avalanchas. | **Corregido.** Sin texto ajeno y una prueba cada vez. |
| WIN-15 | Info | Los logs se escribían al instalar aunque estuvieran desactivados y no ocultaban la ruta corta del perfil. | **Corregido.** |
| WIN-16 | Info | `/path="D:\"` en GOG Galaxy escapaba la comilla. | **Corregido.** |
| WIN-17 · PUB-15 | Info | Conductas que la heurística antivirus asocia a malware; nombre real en los commits. | Pendiente: firmar. Decidir la identidad de los commits públicos. |
| PUB-06 | Baja | El SHA-256 en el mismo release protegía poco; tests y compilación compartían trabajo; los PR publicaban binarios. | **Corregido.** Atestación de procedencia, compilación aparte, sin artefactos de PR, etiqueta solo en `main` y dependencias bloqueadas. Pendiente: activar releases inmutables y una regla para las etiquetas `v*`. |
| PUB-07 | Baja | Inyección de `${{ github.ref_name }}` en PowerShell. | **Corregido.** Va por variable de entorno y se valida. |
| PUB-08 | Baja | Acciones por etiqueta mutable y con Node 20. | **Corregido.** Fijadas por commit, Node 24, Dependabot, sin credenciales persistentes, con límites de tiempo. |
| PUB-09 | Baja | Un `.exe` compilado a mano llevaba rutas locales y el commit del repositorio privado. | **Corregido.** `PathMap`, sin SourceLink y versión sin commit. |
| PUB-11 | Baja | Compilación reproducible no verificable. | **Mejorado.** SDK fijado y `dotnet --info` en el log. |
| PUB-12 | Baja | Faltaban `*.binlog`, `*.pvk` y otros en `.gitignore`. | **Corregido.** |
| PUB-13 | Baja | `New-TestIso.ps1`: rutas relativas, sobrescritura y codificación. | **Corregido.** |
| PUB-14 | Info | Documentación con referencias a documentos internos y promesas que el código no cumplía. | **Corregido.** |

## Seguimiento: fugas al publicar (2026-10-07)

Segunda pasada antes de hacer público el repositorio:

- La documentación ya no menciona detalles internos de la tienda. El requisito
  para quien genere manifiestos es genérico (FORMATO-DISCO §6).
- `tools/check-publicacion.py` comprueba en CI que lo publicable no contenga:
  - caracteres invisibles o bidi;
  - rutas locales;
  - correos;
  - claves ni tokens.

  Se encontró y corrigió así un BOM literal que la propia herramienta de
  edición había colado en un script.
- La exportación del propietario añade una lista negra privada y crea el
  repositorio sin el historial del repositorio de origen.

## Riesgos aceptados

- **Lo que carga Windows antes de `Main`.** No se puede evitar desde el código.
  La defensa es la firma Authenticode.
- **Un disco puede decir que es cualquier juego.** Solo puede abrir lo que ya
  está instalado o lo que el usuario acepte en el diálogo. Firmar los
  manifiestos (v0.3) cerraría este hueco.
- **Un proceso del mismo usuario e integridad** puede cambiar
  `settings.json`/`games.json` o enviar órdenes. No es una frontera de
  seguridad: ese proceso ya puede ejecutar lo que quiera.
