# Iberia Smart Disc

**Primera versión pública: 0.0.1ab · BETA.** Puede contener errores; consulta
las [notas de la versión](docs/releases/0.0.1ab.md).

Mete un disco de [Iberia Custom DVDs](https://iberiacustomdvds.es) en el PC y
se abre el juego, como en una consola.

Iberia Smart Disc es un programa pequeño para Windows que se queda en segundo
plano. Cuando metes un CD, DVD o Blu-ray con un archivo `iberia-disc.json`,
busca el juego en Steam, GOG, Epic Games o en la carpeta que elijas, y lo abre.

**Gratis para uso personal y no comercial.** Su venta y explotación comercial
están prohibidas; consulta la [licencia](LICENSE).

- **Ligero**: un solo `.exe` de menos de 1 MB, sin navegador ni dependencias.
  En espera no usa CPU ni red.
- **Sin instalar nada más**: usa .NET Framework 4.8, que ya viene con Windows 10 y 11.
- **Sin administrador**: se instala solo para tu usuario.
- **Privado**: sin cuentas, sin publicidad, sin telemetría y sin conexión a Internet.
- **Seguro**: nunca ejecuta archivos al azar del disco. Si un disco trae su
  propio programa, pregunta cada vez antes de abrirlo. Al encender el PC, al
  volver de suspensión o con la sesión bloqueada no abre nada sin preguntar.
- **Fácil de quitar**: se desinstala desde el propio programa o desde
  Configuración → Aplicaciones. Solo puede quedar en `%TEMP%` una copia
  apartada del `.exe`, que Windows limpia con los demás temporales.

## Descargar

[**Descargar 0.0.1ab · BETA**](https://github.com/Davidvx98/iberia-smart-disc/releases/download/v0.0.1ab/IberiaSmartDisc.exe)
· [Descarga en la web oficial](https://iberiacustomdvds.es/launcher)
· [Todas las versiones](https://github.com/Davidvx98/iberia-smart-disc/releases)

Requisitos: Windows 10 (versión 1903 o posterior) u 11.

1. Abre `IberiaSmartDisc.exe` y pulsa **Instalar**.
2. Mete un disco de Iberia Custom DVDs.
3. La primera vez, si el juego está en varias plataformas, elige con cuál
   abrirlo. A partir de ahí, meter el disco basta.

Para actualizar, descarga la versión nueva y ábrela: conserva tu configuración.

> **Mientras el ejecutable no esté firmado:**
> - Descárgalo solo de estos dos sitios: [Releases](https://github.com/Davidvx98/iberia-smart-disc/releases)
>   o <https://iberiacustomdvds.es>. Nunca de otra web ni de un enlace que te pasen.
> - Comprueba la descarga como explica [SECURITY.md](SECURITY.md#cómo-verificar-una-descarga).
> - Windows SmartScreen mostrará «Windows protegió su PC» hasta que el programa
>   gane reputación.
> - En Windows 11 con **Smart App Control** activado, Windows bloquea los
>   programas sin firmar y no hay forma de abrirlo. Habrá que esperar a la
>   versión firmada.

## Cómo funciona

```text
Metes el disco ─► Windows avisa ─► se lee iberia-disc.json ─► ¿dónde está el juego?
                                                              Steam · GOG · Epic · tu .exe · el disco
                                                                         │
                                       aviso con cuenta atrás (cancelable) ◄┘
                                                                         │
                                                                    a jugar
```

Los discos solo identifican el juego: tu instalación sigue siendo la tuya, en
la unidad y la plataforma que quieras. Lo que el programa recuerda (qué
instalación usar para cada juego) se guarda en tu PC, no en el disco.

## Para quien hace los discos

- Formato del manifiesto: [docs/FORMATO-DISCO.md](docs/FORMATO-DISCO.md)
- Esquema JSON: [schema/iberia-disc.schema.json](schema/iberia-disc.schema.json)
- Ejemplos: [examples/](examples/)

## Para desarrolladores

Con el [SDK de .NET 10](https://dotnet.microsoft.com/download) en Windows,
Linux o macOS:

```bash
dotnet test tests/IberiaSmartDisc.Core.Tests     # tests
dotnet build src/IberiaSmartDisc -c Release       # → src/IberiaSmartDisc/bin/Release/net48/IberiaSmartDisc.exe
```

- Arquitectura, decisiones y riesgos: [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md)
- Plan de pruebas: [docs/PRUEBAS.md](docs/PRUEBAS.md)
- Opciones de línea de comandos: `IberiaSmartDisc.exe --help`. Las útiles para
  probar son `--portable` (sin instalar) y `--test-disc <carpeta>` (simula un disco).
- Antes de subir cambios: `python tools/check-schema.py` (esquema frente al
  validador) y `python tools/check-publicacion.py` (nada de rutas locales,
  correos, claves ni caracteres invisibles). CI ejecuta los dos.
- Agentes de Claude Code para auditar el proyecto: `.claude/agents/` y su
  contexto en `.claude/contexto-launcher.md`.

Las versiones se publican creando una etiqueta que coincida con
`<ReleaseVersion>` en `Directory.Build.props` (por ejemplo, `v0.0.1ab`).
`<ReleaseChannel>BETA</ReleaseChannel>` marca la publicación como preliminar
y muestra BETA en la interfaz. GitHub Actions compila, prueba y sube el `.exe`
con su SHA-256. La versión interna de Windows es numérica y la de NuGet usa
SemVer; la versión pública del ejecutable conserva `0.0.1ab`.

Solo se distribuye el `.exe` que compila GitHub Actions. No publiques un `.exe`
compilado a mano ni subas la carpeta con el subidor web de GitHub: no respeta
`.gitignore` y arrastraría `bin/` y `obj/`.

## Seguridad

Cómo avisar de una vulnerabilidad y cómo verificar una descarga:
[SECURITY.md](SECURITY.md). Este repositorio no contiene ni necesita
credenciales; las claves de firma de código nunca se suben (ver `.gitignore`).

## Licencia

[Licencia Iberia Smart Disc de Uso Personal No Comercial, versión 1.0](LICENSE).
Puedes usar, estudiar y modificar el programa para tu uso personal, y compartir
copias o forks gratuitamente bajo la misma licencia. No se permite venderlo,
monetizarlo, incorporarlo a productos comerciales ni usarlo con fines
profesionales, empresariales o institucionales sin autorización escrita de
los titulares. Las modificaciones conservan estas restricciones.

El código es público, con derechos de uso limitados por esta licencia.
Las distribuciones incluyen `LICENSE`, que también queda integrado en el `.exe`.
