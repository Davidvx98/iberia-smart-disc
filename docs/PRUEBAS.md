# Cómo se prueba Iberia Smart Disc

## 1. Tests automáticos (cualquier sistema operativo)

```bash
dotnet test tests/IberiaSmartDisc.Core.Tests
```

Cubren el núcleo, que no depende de Windows:

| Área | Qué se comprueba |
|---|---|
| JSON | Lector estricto (comas finales, comentarios, ceros a la izquierda, controles sin escapar, claves repetidas, profundidad) y escritor (ida y vuelta). |
| VDF | `libraryfolders.vdf` nuevo y antiguo, comentarios, condicionales `[$WIN32]`, llaves sin cerrar. |
| Manifiesto | Ejemplos de `examples/`, formato plano antiguo, varios AppID, formato futuro → «Actualiza», campo culpable de cada error, **rutas que intentan salir del disco**, tamaño, UTF-8, hash. |
| Steam | Bibliotecas adicionales, formato antiguo, carpetas borradas o `installdir` peligroso, Steam ausente. |
| Epic | Casar por `AppName`, ignorar DLC y `.item` rotos, URI moderna y antigua. |
| GOG | Registro, apertura directa o con Galaxy, juegos copiados a mano (`goggame-*.info`). |
| Disco | Solo el archivo declarado; instaladores siempre con confirmación. |
| Resolución | Prioridad, «Usar siempre», instalación desaparecida, plataforma desinstalada, plataforma que falla. |
| Configuración | Ida y vuelta, archivos editados a mano, confianza ligada al hash, escritura atómica, archivos dañados, copia de seguridad. |
| Búsqueda local | Pistas del disco, herramientas ignoradas, búsqueda poco profunda, cancelación y límites. |
| Seguridad | Un test por hallazgo de la auditoría del 2026-10-07 (`SecurityRegressionTests`): caracteres bidi e invisibles, instaladores sin argumentos, nombres de dispositivo, saltos de línea finales, cabeceras de imagen, carpetas de GOG compartidas, rutas de red. |
| Esquema | `tests/fixtures/manifests`: el programa y `schema/iberia-disc.schema.json` dan el mismo veredicto (`ManifestCorpusTests` y `python tools/check-schema.py`). |

GitHub Actions los ejecuta en Windows en cada *push* y en cada *pull request*.

## 2. Probar sin grabar discos

1. Copiar `examples/smart-disc` a una carpeta y cambiar el AppID por un juego
   que tengas en Steam.
2. Abrir **Configuración → Avanzado → Probar con una carpeta…** o ejecutar
   `IberiaSmartDisc.exe --test-disc "C:\Pruebas\smart-disc"`.
3. Las pruebas con carpeta siempre preguntan antes de abrir nada.

## 3. Prueba realista con una ISO montada

```powershell
powershell -ExecutionPolicy Bypass -File tools\New-TestIso.ps1 -Source C:\Pruebas\smart-disc -Output C:\Pruebas\portal2.iso
```

Doble clic en la ISO para montarla: Windows crea un lector de DVD virtual y
envía el mismo aviso que con un disco real. Expulsar la unidad en el
Explorador equivale a sacar el disco.

## 4. Lista de comprobación manual (Windows 10 22H2 y Windows 11)

Instalación
- [ ] Doble clic al `.exe` descargado → ventana «Instalar» → no pide administrador.
- [ ] «Ejecutar como administrador» → se niega a funcionar.
- [ ] Con Process Monitor, filtrando por `IberiaSmartDisc.exe` y `NAME NOT FOUND`: ninguna DLL se busca en Descargas después de arrancar.
- [ ] Aparece en el menú Inicio y en **Configuración → Aplicaciones instaladas**.
- [ ] Tras reiniciar, arranca solo y sin ventana (Administrador de tareas → Aplicaciones de arranque).
- [ ] Volver a abrir el `.exe` descargado abre la ventana, no reinstala.
- [ ] Una versión más nueva ofrece «Actualizar» y conserva juegos y ajustes.
- [ ] Desactivar el inicio en el Administrador de tareas y actualizar → sigue desactivado.

Detección
- [ ] Meter un disco con el PC en uso → aviso abajo a la derecha con cuenta atrás → se abre el juego.
- [ ] «Cancelar» y sacar el disco durante la cuenta atrás → no se abre nada.
- [ ] Encender el PC con el disco dentro → pregunta «¿Jugar a…?» y no abre nada solo.
- [ ] Suspender y reanudar con el disco dentro → pregunta.
- [ ] Un CD de música o un DVD sin manifiesto → no pasa nada.
- [ ] Lector USB: conectarlo ya con el disco dentro.
- [ ] Dos lectores con dos discos → dos avisos, uno detrás de otro.
- [ ] «Pausar detección» desde la bandeja → no reacciona.
- [ ] Bloquear la sesión (Win+L), meter un disco y desbloquear → pregunta, no abre solo.
- [ ] Bloquear durante la cuenta atrás → se cancela.

Plataformas
- [ ] Steam en `C:` y en otra unidad; juego en una biblioteca adicional.
- [ ] Juego en Steam y GOG → pregunta, recuerda «Usar siempre».
- [ ] Mover o desinstalar el juego elegido → «La instalación configurada ya no existe».
- [ ] Juego no instalado → «Instalar con Steam» abre el diálogo de Steam.
- [ ] «Seleccionar ejecutable…» con un juego DRM-free; «Buscar automáticamente».
- [ ] Epic y GOG Galaxy (si están disponibles).
- [ ] Disco `data-disc` → pregunta cada vez, con el nombre del archivo y «Origen no verificado»; Intro cancela y «Abrir programa» tarda un momento en activarse.
- [ ] Con el diálogo del disco abierto, cambiar de disco (o de ISO) y aceptar → no se abre nada.
- [ ] Juego ya abierto → lo trae al frente.

Interfaz
- [ ] Escala 100 %, 150 % y 200 %; monitor 2K/4K.
- [ ] Ocultar el icono de la bandeja y reabrir desde el menú Inicio.
- [ ] Navegar con teclado (Tab, Espacio, Intro, Esc) en todas las ventanas.

Desinstalación
- [ ] Desde la ventana, desde la bandeja y desde Aplicaciones instaladas.
- [ ] Desaparecen `Run`, acceso directo, entrada de Aplicaciones y `%LOCALAPPDATA%\IberiaSmartDisc`.
- [ ] Desmarcar «Eliminar juegos recordados» → al reinstalar se conservan.

## 5. Comprobar que el .exe publicado es reproducible

Con el mismo SDK que indica `global.json`, desde un *checkout* de la etiqueta:

```powershell
dotnet build src/IberiaSmartDisc -c Release -p:ContinuousIntegrationBuild=true
Get-FileHash src/IberiaSmartDisc/bin/Release/net48/IberiaSmartDisc.exe
```

El hash debe coincidir con el de Releases mientras el `.exe` no esté firmado
(la firma cambia el archivo). `gh attestation verify` comprueba además que lo
compiló GitHub Actions desde este repositorio.

## 6. Medir consumo

```powershell
Get-Process IberiaSmartDisc | Select-Object CPU, WorkingSet64, PrivateMemorySize64
```

Medir en reposo tras cerrar la ventana, y anotar los resultados en
ARQUITECTURA.md §8.
