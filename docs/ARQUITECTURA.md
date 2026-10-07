# Iberia Smart Disc — Arquitectura y decisiones

Arquitectura, decisiones técnicas, modelo de seguridad, riesgos en Windows
10/11, estado del MVP e ideas nuevas. El formato del disco está en
[FORMATO-DISCO.md](FORMATO-DISCO.md) y las pruebas en [PRUEBAS.md](PRUEBAS.md).

## 1. Decisiones en una tabla

| Tema | Decisión | Motivo |
|---|---|---|
| Runtime | **.NET Framework 4.8** | Viene de serie en Windows 10 (1903+) y Windows 11. El usuario no instala nada más. |
| Interfaz | **WinForms** con tema propio | Ligero, maduro, sin navegador ni XAML. Arranca rápido y gasta poca memoria. |
| Distribución | **Un único `.exe` de ~0,6 MB** | Sin instalador aparte ni DLL: el núcleo se compila dentro del ejecutable. |
| Dependencias | **Ninguna** (JSON y VDF propios) | Nada que actualizar, nada que pueda romper el archivo único. |
| Permisos | **Nunca administrador** (`asInvoker`) | Todo vive en `%LOCALAPPDATA%` y `HKCU`. |
| Inicio con Windows | `HKCU\...\CurrentVersion\Run` | Sin tareas programadas, fácil de quitar y respeta el Administrador de tareas. |
| Detección | `WM_DEVICECHANGE` en ventana oculta | Cero CPU en espera; sin sondeo salvo «modo compatibilidad». |
| Red | **Ninguna** | Funciona offline; no hay telemetría ni comprobación de versiones. |

### 1.1. Por qué no .NET 10, NativeAOT, WPF o WinUI 3

| Opción | Tamaño para el usuario | Problema |
|---|---|---|
| .NET 10 dependiente del runtime | ~0,2 MB | El usuario medio tendría que instalar el «.NET Desktop Runtime» antes. |
| .NET 10 autocontenido (un solo archivo) | 60-150 MB | Muy por encima del objetivo de 20 MB. WinForms/WPF no admiten *trimming*. |
| .NET 10 NativeAOT | 3-8 MB | No admite WinForms ni WPF: habría que dibujar la interfaz con Win32 a mano. Tampoco se puede compilar desde Linux/macOS. |
| WPF | — | Más memoria y arranque más lento para una ventana pequeña; mismo problema de runtime. |
| WinUI 3 | — | Necesita Windows App SDK (runtime aparte o MSIX). Pesado para un programa residente. |
| **.NET Framework 4.8 + WinForms** | **~0,6 MB** | Framework sin novedades, pero soportado mientras lo esté Windows. |

El núcleo (`IberiaSmartDisc.Core`) es `netstandard2.0` y no depende de Windows.
Si algún día compensa pasar a .NET 10 + NativeAOT, solo habría que rehacer la
capa de interfaz y las llamadas a Windows; la lógica y los tests se mantienen.

## 2. Estructura

```text
iberia-smart-disc/
├── src/
│   ├── IberiaSmartDisc.Core/        Lógica sin Windows (netstandard2.0, probada en CI)
│   │   ├── Json/                    Lector/escritor JSON estricto
│   │   ├── Vdf/                     Lector KeyValues de Steam
│   │   ├── Manifest/                iberia-disc.json: modelo, reglas, rutas seguras
│   │   ├── Platforms/               Steam, Epic, GOG, Disc, búsqueda local
│   │   ├── Resolution/              GameResolver: candidatos + prioridad + preferencia
│   │   └── Configuration/           settings.json, games.json, copia de seguridad
│   └── IberiaSmartDisc/             Aplicación Windows (net48, WinForms)
│       ├── Hosting/                 Instancia única, ventana oculta, órdenes, estado
│       ├── Discs/                   Monitor de lectores, coordinador, caché
│       ├── Launching/               Abrir enlaces de plataforma y ejecutables
│       ├── Setup/                   Instalar, actualizar, desinstalar, Run, accesos
│       ├── UI/                      Ventanas, avisos, tema
│       ├── Native/                  Llamadas a Win32
│       └── Logging/
├── tests/IberiaSmartDisc.Core.Tests/
├── schema/iberia-disc.schema.json   Esquema del manifiesto (lo usa también la web)
├── examples/                        Discos de ejemplo
└── tools/New-TestIso.ps1            ISO de prueba para montar en Windows
```

Separa detección de discos, manifiesto, plataformas, configuración, inicio con
Windows, desinstalación, interfaz y logs, con nombres en inglés para el código.

### 2.1. Separación identificar / resolver / lanzar

```text
iberia-disc.json ──► ManifestParser ──► DiscManifest          (qué disco es)
                                            │
          AppSettings + GameRecord ──► GameResolver
                                            │  pregunta a cada IPlatformProvider
                                            ▼
                               ResolutionResult               (con qué se puede abrir)
                                 · Candidates (ordenados por prioridad)
                                 · Preferred / PreferredMissing
                                 · InstallActions
                                            │
                                     DiscCoordinator          (qué hacer y si preguntar)
                                            │
                                      GameLauncher            (abrirlo)
```

Añadir una plataforma (EA App, Ubisoft Connect, Battle.net, itch.io…) es crear
una clase que implemente `IPlatformProvider` y registrarla en `AppState`.

## 3. Detección de discos

1. Una **ventana oculta de nivel superior** recibe `WM_DEVICECHANGE`. Windows
   envía los avisos de volúmenes (`DBT_DEVICEARRIVAL` / `DBT_DEVTYP_VOLUME`) a
   todas las ventanas de nivel superior, pero **no** a las ventanas «solo
   mensajes» (`HWND_MESSAGE`); por eso no se usa una de esas.
2. El `unitmask` indica las letras afectadas. Solo se atienden unidades con
   `DriveType == CDRom`: lectores internos, **lectores USB** y **ISO montadas**.
   Un pendrive con un `iberia-disc.json` se ignora. Una ISO montada **sí** cuenta
   como disco: cualquiera puede preparar una y el usuario la monta con doble
   clic, por eso nada del disco se ejecuta sin preguntar (apartado 4).
3. Tras el aviso se espera a que la unidad esté lista (hasta 25 s: lectores USB
   y Blu-ray tardan) y se lee `<unidad>:\iberia-disc.json` (máx. 64 KB, UTF-8).
4. `SetErrorMode(SEM_FAILCRITICALERRORS)` evita el diálogo «No hay ningún disco
   en la unidad» al consultar lectores vacíos.
5. Al arrancar se revisan los lectores que ya tienen disco.
6. Al expulsar (`DBT_DEVICEREMOVECOMPLETE`) se cancela el aviso de ese disco.
7. Los discos se procesan **de uno en uno** (dos lectores no abren dos diálogos)
   y se descartan avisos duplicados del mismo disco durante 20 s.
8. Si hay otra sesión de Windows activa (cambio rápido de usuario), la sesión
   en segundo plano no reacciona. Con la sesión **bloqueada** no se abre nada:
   los discos que lleguen se revisan al desbloquear, preguntando, y una cuenta
   atrás en curso se cancela al bloquear.
9. **Modo compatibilidad** (desactivado por defecto): cada 5 s compara el número
   de serie de cada volumen óptico, para lectores raros que no avisan. En
   unidades vacías la consulta falla al instante y no hace girar el lector.

### 3.1. El disco que se quedó dentro

Para evitar que el PC abra un juego pesado nada más encender:

| Situación | Comportamiento |
|---|---|
| Disco ya dentro al arrancar el programa | Aviso **«¿Jugar a…?»** con «Jugar» / «Ahora no». Nunca abre solo. |
| Disco detectado en los primeros 90 s tras iniciar sesión | Igual: pregunta. |
| Disco detectado en los 60 s tras volver de suspensión | Igual: pregunta. |
| Disco metido con la sesión bloqueada | Nada; al desbloquear, pregunta. |
| Disco metido con el PC en uso | Aviso con **cuenta atrás de 5 s** (configurable 0-30) y «Cancelar». |
| «Abrir al insertar» desactivado | Siempre pregunta. |

El aviso aparece abajo a la derecha, con la carátula, y **no roba el foco**: si
el usuario está escribiendo o jugando, no le saca de lo que hace.

## 4. Plataformas

### Steam
- Carpeta: preferencia manual → `HKCU\Software\Valve\Steam\SteamPath` →
  `HKLM\SOFTWARE\(WOW6432Node\)Valve\Steam\InstallPath` → `Program Files (x86)\Steam`
  → `X:\Steam` y `X:\Program Files (x86)\Steam` en cada unidad fija.
- Bibliotecas: la propia carpeta, `steamapps\libraryfolders.vdf` (formato nuevo
  con `"path"` y antiguo con `"1" "D:\\SteamLibrary"`), las carpetas que añade
  el usuario y `X:\SteamLibrary` en cada unidad.
- Instalado: existe `steamapps\appmanifest_<AppID>.acf` y su carpeta
  `steamapps\common\<installdir>`. Si `StateFlags` no incluye «instalado», se
  muestra igualmente (Steam lo actualiza al abrir).
- Abrir: `steam://rungameid/<AppID>`. Instalar: `steam://install/<AppID>`
  (abre el diálogo de Steam; no descarga nada sin confirmar).
- Un juego puede tener varios AppID (p. ej. ediciones *legacy* y *enhanced*).

### Epic Games
- Manifiestos `*.item` (JSON) en `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests`
  o en `AppDataPath` del registro. Se casa por `AppName` o `CatalogItemId` y se
  descartan DLC.
- Abrir: `com.epicgames.launcher://apps/<namespace>%3A<item>%3A<appName>?action=launch&silent=true`
  (o el formato antiguo `apps/<appName>` si faltan datos).

### GOG
- `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<productId>` (`exe`, `path`,
  `launchParam`, `workingDir`). Lo escriben los instaladores con y sin Galaxy.
- Si se copió la carpeta a mano: `goggame-<productId>.info`, pero solo en las
  carpetas de juegos que añadió el usuario y en `Program Files (x86)\GOG Galaxy\Games`.
  No en `X:\GOG Games`: en la raíz de `C:` y en discos secundarios cualquier
  cuenta del PC puede crear carpetas, y un `.info` falso abriría su programa en
  la sesión de otro usuario.
- Por defecto abre el ejecutable directamente (DRM-free, instantáneo). Opción
  para abrir a través de GOG Galaxy.

### Ejecutable local
- Cualquier `.exe` elegido por el usuario; no se intenta averiguar su origen.
- «Buscar automáticamente» solo mira carpetas de juegos conocidas y las
  del usuario, con límites de profundidad (≤4), carpetas (6.000) y tiempo (15 s).
  Usa las pistas del disco (`local.executables`, `local.folders`), con el mismo
  filtro de desinstaladores y herramientas. **Nunca ejecuta lo que encuentra**
  ni lo deja marcado: el usuario revisa la ruta y elige.
- Las rutas de red (`\\servidor\…`) se ignoran en bibliotecas y carpetas: tocarlas
  al meter un disco abriría conexiones SMB.

### Desde el disco (data-disc)
- Solo el archivo declarado en `discLaunch`, con la ruta validada para que no
  salga del disco.
- **Se pregunta cada vez**, también con «Abrir al insertar» activado. El
  diálogo muestra el nombre del archivo aparte, la carpeta, los argumentos y
  «Origen no verificado». Cancelar es el botón por defecto y el de abrir tarda
  un momento en activarse.
- No se recuerda la confianza: el `iberia-disc.json` de un disco legítimo se
  puede copiar a otro disco o ISO junto con otro programa, así que no
  identifica nada. Recordarla con seguridad exige firmar los manifiestos (idea
  de la v0.3, apartado 12).
- Justo antes de abrir se comprueba que sigue siendo el mismo disco (número de
  serie del volumen y hash del manifiesto). Si se cambió o expulsó, no se abre.
- Los instaladores del disco no admiten argumentos.
- Los textos y rutas del manifiesto no pueden llevar caracteres de control,
  marcas de dirección (bidi) ni caracteres invisibles, para que el diálogo no
  pueda mostrar `Manualexe.pdf` cuando el archivo es un `.exe`.

## 5. Resolución y prioridad

1. Si el juego tiene una opción guardada con «Usar siempre» y sigue existiendo → esa.
2. Si existía pero ya no (carpeta movida, plataforma desinstalada) → diálogo
   «La instalación configurada ya no existe» / «Steam no está disponible».
3. Si hay una sola instalación → esa.
4. Si hay varias → «¿Con qué instalación quieres abrirlo?» con la de mayor
   prioridad marcada y «Usar siempre esta opción» activado (se puede desactivar
   la pregunta en Configuración).
5. Si no hay ninguna → «No encontramos el juego instalado» con «Instalar con
   Steam/GOG/Epic», «Instalar desde el disco», «Buscar automáticamente»,
   «Seleccionar ejecutable…» y «Configurar plataformas».

El aviso y la lista muestran el nombre que da la plataforma (p. ej.
«Steam · Portal 2» según el `appmanifest`): un disco puede llamarse como
quiera, pero así se ve qué se va a abrir de verdad.

Prioridad por defecto: Disco → Steam → GOG → Epic → Ejecutable local
(configurable). Una plataforma que falla (registro raro, JSON roto) no impide
abrir el juego con otra.

## 6. Almacenamiento local

```text
%LOCALAPPDATA%\IberiaSmartDisc\
├── IberiaSmartDisc.exe
├── settings.json        Preferencias
├── games.json           Juegos recordados, preferencias e historial
├── cache\covers\        Carátulas reducidas a 512 px (para mostrarlas sin el disco)
├── cache\manifests\     Último manifiesto de cada juego (para configurarlo sin disco)
└── logs\                Un archivo por día, 14 días, 1 MB máx., sin el nombre de usuario
```

- Escritura atómica (temporal + reemplazo): un corte de luz no deja archivos a medias.
- Un archivo dañado se aparta como `*.corrupt-<fecha>` y se sigue con valores por defecto.
- Lectura tolerante: un valor raro vuelve a su valor por defecto.
- Exportar/importar en un único JSON (copias de seguridad y otros PC). Al
  importar no se traen ejecutables locales: un archivo ajeno no debe dejar
  programas que se abran al meter un disco.
- **Modo portátil** (`--portable`): datos en `IberiaSmartDisc-datos\`
  junto al `.exe`, sin tocar el registro.

## 7. Instalación, actualización y desinstalación

**Instalar** (doble clic al `.exe` descargado):
copia a `%LOCALAPPDATA%\IberiaSmartDisc\` (escribiendo un archivo nuevo, sin la
marca «descargado de Internet»), crea `settings.json`, registra `Run`, crea el
acceso del menú Inicio y la entrada en **Configuración → Aplicaciones
instaladas** (por usuario, sin admin) y arranca la copia instalada.

> **No hace falta administrador nunca**: `HKCU\...\Run`, `HKCU\...\Uninstall` y
> `%LOCALAPPDATA%` son del propio usuario. Si se abre con «Ejecutar como
> administrador» (UAC activo), el programa se niega: elevado abriría juegos y
> programas como administrador con datos que cualquier programa del usuario
> puede cambiar.

La primera instrucción del programa limita la búsqueda de DLL a System32
(`SetDefaultDllDirectories`) y las llamadas nativas propias también
(`DefaultDllImportSearchPaths`). Así una DLL dejada en Descargas junto al
instalador no se carga. Queda fuera de su alcance lo que Windows y .NET cargan
antes de `Main`; la firma Authenticode es la defensa para eso.

**Abrir otra vez el `.exe` descargado**: si es la misma versión, abre la
ventana de la copia instalada; si es más nueva, ofrece **actualizar** (cierra
la instancia residente, sustituye el ejecutable y conserva la configuración).
Así la actualización es «descargar la última versión y abrirla». Actualizar no
reactiva el inicio automático si el usuario lo desactivó en el Administrador de
tareas.

Las órdenes entre instancias (abrir la ventana, salir…) solo se envían a una
ventana cuyo proceso sea de verdad el programa instalado: otro programa podría
crear una ventana con el mismo título. Para cerrar la instancia residente se
espera al proceso real, no al nombre de instancia, para que ocuparlo no
bloquee actualizar ni desinstalar.

**Desinstalar** (botón, menú de la bandeja o Aplicaciones instaladas): diálogo
con casillas para elegir qué datos borrar. Borra `Run`, el permiso de `StartupApproved`, el
acceso directo, la entrada de desinstalación, los datos elegidos y el
ejecutable. Windows no deja borrar un `.exe` en uso, pero sí **moverlo** dentro
del mismo disco: se aparta a `%TEMP%` y la carpeta queda vacía. Si `%TEMP%`
está en otro disco, una copia auxiliar (`--uninstall-helper`) espera a que
termine el proceso y lo borra. Esa copia solo actúa si su proceso padre es el
programa instalado y solo borra la ruta fija de instalación. El resto en
`%TEMP%` (~0,6 MB) lo limpia Windows como cualquier otro temporal, igual que
hacen los desinstaladores NSIS, o la siguiente instalación.

## 8. Recursos

- En espera: **0 % de CPU**. No hay temporizadores ni sondeo (salvo modo
  compatibilidad); la ventana oculta solo despierta con avisos de Windows.
- Memoria: al cerrar ventanas se liberan y se devuelve memoria a Windows.
  *Pendiente de medir en Windows real* (la estimación para un proceso WinForms
  en reposo es de 15-25 MB privados).
- Disco: nada en espera. Red: nada nunca.
- Ejecutable: ~0,6 MB (la mitad son el icono y el logo).

## 9. Antivirus y SmartScreen

Un programa que se copia a `AppData`, se registra en `Run` y se borra a sí
mismo se parece, en heurística, a malware. Medidas:

- Sin empaquetadores, ofuscación ni compresión del ejecutable.
- No lanza `cmd.exe` ni PowerShell (la autoeliminación usa un simple
  renombrado; la copia auxiliar solo existe si `%TEMP%` está en otro disco).
- No descarga nada, no abre puertos, no inyecta nada, no pide administrador.
- Información de versión, editor e icono incrustados; código público y
  compilación reproducible en GitHub Actions con SHA-256 publicado.
- **Firmar con Authenticode antes de anunciar la descarga.** Sin firma,
  SmartScreen avisa y Smart App Control de Windows 11 lo bloquea sin opción. Las
  condiciones de cada vía cambian; las de abajo son las que revisó la auditoría
  del 2026-10-07 y hay que confirmarlas antes de decidir:
  - Azure Artifact Signing (antes Trusted Signing), unos 10 $/mes: solo
    empresas de EE. UU., Canadá, UE y Reino Unido, y particulares de EE. UU. y
    Canadá. Un autónomo en España no es elegible.
  - Certificado OV en un token o HSM (150-300 $/año). Los EV ya no saltan
    SmartScreen desde 2024.
  - [SignPath Foundation](https://signpath.org): gratis para código abierto,
    pero exige licencia OSI, publicar antes sin firmar, roles con MFA y una
    página de política de firma; el editor visible es «SignPath Foundation».
- Enviar cada versión a Microsoft para análisis de falsos positivos
  (https://www.microsoft.com/wdsi/filesubmission).
- Publicación: GitHub Actions compila el `.exe` en un trabajo sin los paquetes
  de los tests, con acciones fijadas por commit, y genera una atestación de
  procedencia (`gh attestation verify`). Conviene activar en el repositorio los
  *releases* inmutables y una regla que proteja las etiquetas `v*`.

## 10. Riesgos conocidos en Windows 10 y 11

| Riesgo | Mitigación |
|---|---|
| Ventanas `HWND_MESSAGE` no reciben avisos de volúmenes | Se usa una ventana oculta de nivel superior. |
| Algunos lectores/controladores no avisan al insertar (MCN desactivado, `cdrom\AutoRun=0`) | Modo compatibilidad opcional; diagnóstico en «Plataformas». |
| El lector tarda en montar el disco | Espera de hasta 25 s tras el aviso. |
| Diálogo «No hay disco en la unidad» | `SEM_FAILCRITICALERRORS`. |
| AutoPlay de Windows muestra su propio aviso | Inofensivo. Documentado: se puede poner «No realizar ninguna acción» para DVD. No se cambia por el usuario. |
| Discos grabados en ISO 9660 nivel 1 recortan el nombre (`IBERIA_D.JSO`) | Grabar con UDF o Joliet (ver FORMATO-DISCO.md). |
| Usuario desactiva el inicio desde el Administrador de tareas | Se detecta (`StartupApproved`) y se muestra; no se fuerza. |
| Alguien borra la clave `Run` con otra herramienta | Se respeta: la casilla pasa a desactivada, no se vuelve a crear sola. |
| Cambio rápido de usuario: dos instancias reaccionan al mismo disco | Solo actúa la sesión activa en la consola. |
| Suspensión/hibernación con disco dentro | Periodo de 60 s en el que se pregunta en vez de abrir. |
| DPI alto (150-200 %) | Manifiesto *DPI aware* y tamaños escalados en todas las ventanas. |
| Juego ya abierto | Para ejecutables locales/del disco se trae al frente en vez de abrir otro. Steam/Epic lo gestionan solos. |
| Juego que pide administrador | Se abre con `ShellExecute`: Windows muestra su UAC; si se cancela, no hay error. |
| Steam movido de disco / biblioteca nueva | Se relee `libraryfolders.vdf` en cada inserción (sin caché). |
| Windows 10 anterior a 1903 / LTSC 2019 | Necesitan instalar .NET Framework 4.8 (Windows avisa al abrir el `.exe`). Todas esas versiones están fuera de soporte salvo LTSC. |
| Windows en ARM64 | .NET Framework 4.8.1 es nativo en ARM64; el `.exe` AnyCPU funciona. Sin probar. |
| DLL dejada en Descargas junto al instalador | DLL solo desde System32 desde la primera línea. Lo que se carga antes de `Main`, solo con firma. |
| Abrirlo como administrador | Se niega a funcionar elevado. |
| Disco o ISO preparados con el manifiesto de otro disco | Los programas del disco se confirman siempre; se comprueba que el disco no cambió. |
| PC compartido: otra cuenta crea `C:\GOG Games\…` | Los `.info` de GOG solo se leen en carpetas del usuario o protegidas. |
| Disco metido con la sesión bloqueada | No se abre nada hasta desbloquear. |

## 11. Privacidad

No hay red en todo el programa: ni telemetría, ni comprobación de versiones,
ni cuentas. Los logs no guardan el nombre de la cuenta de Windows
(`%USERPROFILE%`) y se pueden desactivar y borrar. El «informe de diagnóstico»
solo se copia al portapapeles si el usuario lo pide.

## 12. Ideas nuevas

| Idea | Beneficio | Complejidad | Impacto en recursos | ¿MVP? |
|---|---|---|---|---|
| Aviso con carátula y cuenta atrás cancelable, sin robar el foco | Sensación de consola sin sustos | Baja | Ninguno en espera | **Sí (hecho)** |
| Preguntar siempre si el disco ya estaba dentro al arrancar o al volver de suspensión | Evita abrir juegos pesados al encender | Baja | Ninguno | **Sí (hecho)** |
| Cancelar el aviso al expulsar el disco | Natural: sacas el disco y no pasa nada | Baja | Ninguno | **Sí (hecho)** |
| Entrada en «Aplicaciones instaladas» de Windows | Desinstalar como cualquier programa; menos soporte | Baja | Ninguno | **Sí (hecho)** |
| Actualizar abriendo el `.exe` nuevo | Actualización sin autoactualizador ni red | Baja | Ninguno | **Sí (hecho)** |
| `--test-disc` / «Probar con una carpeta» | Probar discos sin grabarlos; soporte remoto | Baja | Ninguno | **Sí (hecho)** |
| Pistas `local.executables` / `local.folders` en el manifiesto | «Buscar automáticamente» rápido y preciso sin escanear el disco duro | Baja | Ninguno en espera | **Sí (hecho)** |
| Exportar/importar configuración | Copias y varios PC | Baja | Ninguno | **Sí (hecho)** |
| Modo compatibilidad por sondeo (opcional) | Lectores que no avisan | Baja | ~0 (una llamada cada 5 s) | **Sí (hecho, desactivado)** |
| Modo portátil | Probar sin instalar, PC de amigos | Baja | Ninguno | **Sí (hecho)** |
| Informe de diagnóstico copiable | Soporte en un mensaje | Baja | Ninguno | **Sí (hecho)** |
| `autorun.inf` con icono y nombre del juego en el disco | El lector muestra la carátula y el título en «Este equipo» | Muy baja (solo disco) | Ninguno | **Sí (en la guía del disco)** |
| QR en la carátula hacia la página de descarga | El cliente encuentra el programa sin buscar | Muy baja (solo impresión) | Ninguno | Sí |
| Firma del manifiesto (ECDSA P-256 sobre los bytes de `iberia-disc.json` con los SHA-256 de los ejecutables, en `iberia-disc.sig`) | «Disco oficial verificado»; es lo único que permitiría abrir data-discs oficiales sin preguntar cada vez | Media | Ninguno | No (v0.3) |
| Firma Authenticode del `.exe` | Menos avisos de SmartScreen y antivirus | Baja (trámite) | Ninguno | **Sí, antes de publicar** |
| Pantalla de carga a pantalla completa con la carátula mientras arranca Steam | Más «consola» | Media | Solo al abrir | No (v0.2) |
| Botón A del mando para «Jugar» en el aviso (XInput solo mientras está visible) | Jugar desde el sofá | Media | Solo con el aviso visible | No |
| Sonido corto al detectar el disco (opcional) | Respuesta inmediata | Baja | Ninguno | No |
| Juegos de varios discos (`discSet`): pedir el siguiente | Colecciones y ediciones grandes | Media | Ninguno | No (el formato ya lo admite) |
| Comprobación de versiones opcional (una vez por semana, sin datos) | Usuarios al día | Media | Una petición semanal | No (va contra «sin red» por defecto) |
| Más plataformas (EA App, Ubisoft Connect, Battle.net, Xbox, itch.io) | Más compatibilidad | Media por plataforma | Ninguno | No |
| Ficha del juego en la web desde «Mis juegos» | Recompra y descubrimiento | Baja | Ninguno | No |

## 13. Estado del MVP

| Requisito | Estado |
|---|---|
| MVP 0.1: Windows 10/11, inicio automático, segundo plano, detección CD/DVD/BD, lectura del manifiesto, Steam, ejecutable local, configuración, bandeja, desinstalación limpia | Escrito y compilado. **Falta probarlo en Windows real** (ver PRUEBAS.md). |
| MVP 0.2: GOG, Epic, varias bibliotecas, búsqueda, historial, carátulas, configuración por juego | Escrito y compilado, adelantado al 0.1. Lógica probada con tests; sin probar en Windows. |
| La tienda genera el `iberia-disc.json` de cada producto | **Pendiente** en el proyecto de la tienda, siguiendo FORMATO-DISCO.md §6. |
| La tienda ofrece la descarga de la última versión | **Pendiente** en el proyecto de la tienda; debe enlazar a Releases y mostrar el SHA-256. |

## 14. Auditoría de seguridad

El 2026-10-07 tres revisiones independientes cubrieron la entrada desde el
disco, la integración con Windows y la publicación. Hallazgos, correcciones y
riesgos aceptados: [auditorias/2026-10-07-seguridad.md](auditorias/2026-10-07-seguridad.md).

## 15. Cómo se compila

Con el SDK de .NET 10 en cualquier sistema operativo:

```bash
dotnet test tests/IberiaSmartDisc.Core.Tests        # tests del núcleo
dotnet build src/IberiaSmartDisc -c Release          # src/IberiaSmartDisc/bin/Release/net48/IberiaSmartDisc.exe
```

GitHub Actions (`.github/workflows/build.yml`) compila y prueba en
`windows-latest` en cada *push* y publica el `.exe` con su SHA-256 en
*Releases* al crear una etiqueta `vX.Y.Z`.
