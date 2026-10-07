# Contexto compartido — launcher Iberia Smart Disc

Punto de entrada de los agentes `launcher-*`. Revisado el 2026-10-07. Confirma
en el código cualquier dato que pueda haber cambiado.

## Qué es y de qué depende

Iberia Smart Disc es una aplicación de Windows que se queda en segundo plano y,
al meter un disco de Iberia Custom DVDs, abre el juego (Steam, GOG, Epic, un
`.exe` local o el programa del propio disco).

Hay **dos proyectos** con el mismo propósito:

| | Tienda | Launcher (este) |
|---|---|---|
| Qué es | La web que vende los discos y los personaliza | La app de Windows que los lee |
| Repositorio | Privado | **Público** |
| Depende de | — | De la tienda: sin tienda no hay discos |
| Contrato | Genera `iberia-disc.json` y ofrece la descarga | `schema/iberia-disc.schema.json` y `docs/FORMATO-DISCO.md` |

El launcher no conoce el código de la tienda. Lo único que comparten es el
formato del manifiesto y la página de descarga. Mientras el launcher viva en
la carpeta `iberia-smart-disc/` dentro del repositorio de la tienda, sus rutas
son relativas a esa carpeta; cuando sea independiente, a la raíz.

**Este repositorio es público.** No se copia aquí nada interno de la tienda:
rutas o paneles, nombres de tablas o archivos del servidor, infraestructura,
dominios de pruebas, datos de clientes ni del propietario. `python
tools/check-publicacion.py` hace las comprobaciones genéricas; el propietario
exporta con una lista negra privada antes de publicar. Si encuentras algo así,
es un hallazgo.

## Mapa del código

| Ruta | Contenido |
|---|---|
| `src/IberiaSmartDisc.Core/` | netstandard2.0, sin Windows: JSON y VDF propios, manifiesto (`Manifest/`), texto seguro (`Common/TextSafety.cs`), cabeceras de imagen, plataformas (Steam, Epic, GOG, Disc, búsqueda local), resolución y configuración. Se compila dentro del `.exe`. |
| `src/IberiaSmartDisc/` | .NET Framework 4.8 + WinForms: `Program.cs` (modos y arranque), `Hosting/` (instancia única, ventana oculta, órdenes, estado), `Discs/` (monitor, coordinador, caché), `Launching/`, `Setup/` (instalar, actualizar, desinstalar, `Run`, acceso directo), `UI/`, `Native/`. |
| `tests/IberiaSmartDisc.Core.Tests/` | xunit del núcleo; `SecurityRegressionTests` tiene un test por hallazgo de seguridad. |
| `tests/fixtures/manifests/` | Corpus compartido entre el validador C# y el esquema. |
| `schema/`, `examples/`, `docs/`, `tools/`, `.github/` | Contrato, ejemplos, documentación, utilidades y CI. |

Documentos: `docs/ARQUITECTURA.md` (decisiones y riesgos), `docs/FORMATO-DISCO.md`,
`docs/PRUEBAS.md`, `docs/auditorias/` y `SECURITY.md`.

## Modelo de amenazas

- **Quien controla un disco o una ISO.** Cualquiera puede grabar uno o
  distribuir una ISO, y una ISO montada con doble clic es un lector de DVD para
  Windows. Es el atacante principal.
- **Archivos junto al `.exe`.** Descargas, `%TEMP%`: DLL o `.config`.
- **Otra cuenta del mismo PC.** Puede crear carpetas en la raíz de `C:` y en
  discos secundarios.
- **Archivos que el usuario importa.** Copias de configuración.
- **Procesos de la misma sesión.** Con integridad igual o menor. No son
  frontera, salvo que el launcher les dé algo que no tenían.
- **La cadena de publicación.** Workflows, acciones, dependencias y releases.

## Invariantes de seguridad

1. Nada de un disco se ejecuta sin confirmación informada **cada vez**: archivo,
   carpeta, argumentos y «Origen no verificado», con Cancelar por defecto.
   Justo antes de abrir se comprueba que el disco no ha cambiado.
2. Solo reaccionan unidades `CDRom`; las rutas del manifiesto no salen del
   disco; textos y rutas sin caracteres de control, bidi ni invisibles.
3. Al arrancar, al volver de suspensión o con la sesión bloqueada no se abre
   nada sin preguntar.
4. Nunca administrador: se niega a funcionar elevado. DLL solo desde System32.
5. Sin red, sin telemetría, sin cuentas. Los logs no guardan el perfil del
   usuario.
6. Las órdenes entre instancias solo van al ejecutable instalado.
7. El esquema y el validador C# dan el mismo veredicto sobre el corpus.
8. El repositorio no contiene secretos ni datos internos de la tienda.

## Comandos

Con el SDK de .NET 10 (en la máquina de la tienda está en `~/.dotnet`: exporta
`DOTNET_ROOT=$HOME/.dotnet`):

| Comando | Uso |
|---|---|
| `dotnet test tests/IberiaSmartDisc.Core.Tests` | Tests del núcleo. Permitido. |
| `dotnet build src/IberiaSmartDisc -c Release` | Compila el `.exe`. Permitido; no lo distribuyas. |
| `python3 tools/check-schema.py` | Esquema frente al corpus (requiere `jsonschema`). |
| `python3 tools/check-publicacion.py` | Fugas en lo publicable. |

No se puede ejecutar la parte de Windows en Linux: lo que dependa de Windows es
«no verificado» o «plausible». No publiques releases, no cambies secretos ni
ajustes del repositorio, no hagas commit ni push sin que se pida.

## Escritura y entrega

- Responde en español. Informe persistente en
  `docs/auditorias/AAAA-MM-DD-<area>.md`; es público, así que nada interno de
  la tienda.
- Estados: **CONFIRMADO** (código trazable o test), **PROBABLE** (falta una
  comprobación concreta, a menudo en Windows) y **OBSERVACIÓN** (mejora sin
  fallo demostrado). Severidad: crítica, alta, media, baja o informativa.
- Cada corrección de seguridad lleva su test en `SecurityRegressionTests` con el
  ID del hallazgo en el nombre. Si cambia el formato del manifiesto, se
  actualizan a la vez el esquema, el corpus y `FORMATO-DISCO.md`.
- Las herramientas de edición pueden convertir los escapes Unicode escritos
  como «barra invertida, u y cuatro cifras» en el carácter real. Construye los
  caracteres especiales por su código (`(char)0x202E` en C#, `chr(0x202E)` en
  Python) y pasa `tools/check-publicacion.py` después.
