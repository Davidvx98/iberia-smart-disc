# Formato del disco: `iberia-disc.json` (formato 1)

Todos los discos de Iberia Custom DVDs llevan en la raíz un archivo
`iberia-disc.json` que dice qué juego representan. Iberia Smart Disc solo
reacciona a discos que lo tengan.

Esquema validable: [`schema/iberia-disc.schema.json`](../schema/iberia-disc.schema.json).
Ejemplos: [`examples/`](../examples/). El esquema y el validador del programa
se comprueban en CI contra el mismo corpus (`tests/fixtures/manifests`): lo que
el esquema acepta, el programa también.

## 1. Reglas del archivo

- Nombre exacto `iberia-disc.json`, en la **raíz** del disco.
- Texto **UTF-8** (con o sin BOM), JSON estricto: sin comentarios ni comas
  finales, sin claves repetidas. Máximo **64 KB**.
- **Grabar el disco con UDF o Joliet** (lo normal en ImgBurn, CDBurnerXP, Nero
  o el grabador de Windows). Con ISO 9660 nivel 1 el nombre se recortaría a
  `IBERIA_D.JSO` y el disco no se reconocería.

## 2. Ejemplo recomendado (Smart Disc)

```json
{
  "iberiaDisc": true,
  "format": 1,
  "discId": "ICD-PORTAL-2-K7P4M",
  "discType": "smart-disc",
  "game": {
    "id": "portal-2",
    "name": "Portal 2",
    "edition": "Iberia Custom Edition"
  },
  "platforms": {
    "steam": { "appId": 620 },
    "gog": { "productId": null },
    "epic": { "appName": null }
  },
  "local": {
    "executables": ["portal2.exe"],
    "folders": ["Portal 2"]
  },
  "artwork": { "cover": "cover.png", "icon": "icon.ico" }
}
```

## 3. Campos

| Campo | Obligatorio | Descripción |
|---|---|---|
| `iberiaDisc` | Sí | Siempre `true`. |
| `format` | Sí | Versión **incompatible** del formato. Hoy `1`. |
| `discId` | No | Copia o edición concreta, p. ej. `ICD-<SLUG>-<código aleatorio>`. Letras, números, `.`, `_`, `-`; máx. 64. Nada de números correlativos (revelan las ventas) ni datos del cliente. |
| `discType` | No | `smart-disc` (por defecto) o `data-disc`. Si hay `discLaunch` y falta, se entiende `data-disc`. |
| `game.id` | Sí | Identificador estable del juego, el mismo que usa la tienda en la URL del producto: minúsculas, dígitos y guiones; máx. 64; no puede ser un nombre de dispositivo de Windows (`con`, `nul`, `com1`…). Es la clave con la que el PC recuerda el juego: no debe cambiar una vez vendido el disco. |
| `game.name` | Sí | Nombre visible: hasta 120 caracteres, con algo más que espacios. Ver §3.1. |
| `game.edition` | No | Se muestra como «Portal 2 · Iberia Custom Edition». |
| `platforms.steam.appId` / `appIds` | No | AppID de Steam (número o texto). Varios si el juego tiene ediciones distintas en Steam. |
| `platforms.gog.productId` / `productIds` | No | ID de producto de GOG. |
| `platforms.epic.appName` | No | `AppName` de Epic. Para ofrecer «Instalar con Epic» hacen falta también `namespace` y `catalogItemId`. |
| `local.executables` | No | Nombres de `.exe` del juego, sin carpetas. Mejoran mucho «Buscar automáticamente». |
| `local.folders` | No | Nombres de carpeta habituales de la instalación. |
| `artwork.cover` | No | Carátula en el disco: PNG, JPEG o BMP (máx. 8 MB y 16 megapíxeles). El programa comprueba el tipo real por la cabecera: un `.png` que sea otra cosa se descarta. *WebP y GIF no*. |
| `artwork.icon` | No | Icono `.ico` en el disco. |
| `discLaunch` | No | Solo en `data-disc`: `{ "mode": "executable" \| "installer", "path": "Game\\Game.exe", "arguments": "…" }`. Los instaladores no admiten `arguments`. |
| `discSet` | No | Juegos de varios discos: `{ "id": "…", "disc": 1, "total": 4 }`. Se lee, la interfaz llegará más adelante. |

Las formas antiguas de los primeros ejemplos (`"gameId"` y `"name"` en la raíz)
también se aceptan. Los valores `null` equivalen a no poner el campo.

### 3.1. Caracteres no admitidos

Ni en textos (`game.name`, `game.edition`, `arguments`) ni en rutas o nombres:
caracteres de control, marcas de dirección (bidi, p. ej. U+202E), caracteres de
ancho cero, separadores de línea, uso privado, etiquetas Unicode ni sustitutos
sueltos. La lista exacta está en `src/IberiaSmartDisc.Core/Common/TextSafety.cs`
y en la clase de caracteres del esquema. Con ellos un disco podría hacer que el
diálogo muestre `Manualexe.pdf` cuando el archivo es `Manual…fdp.exe`. Las
letras con tilde, la ñ, otros alfabetos y los emoji normales sí se admiten.

### 3.2. Rutas dentro del disco

`discLaunch.path`, `artwork.cover` y `artwork.icon` son rutas **relativas a la
raíz del disco**. Se rechazan: rutas absolutas o de red, `..`, `:` (unidades o
flujos alternativos), comodines, nombres reservados (`CON`, `NUL`, `COM1`…),
nombres terminados en punto o espacio y separadores dobles. Se admiten `\` y `/`.

## 4. Versionado

- **Añadir** un campo opcional no cambia `format`: las versiones antiguas del
  programa ignoran lo que no conocen.
- **Solo un cambio incompatible** (cambiar el significado de un campo
  existente, hacer obligatorio uno nuevo) sube `format` a 2. Un programa que
  solo entiende el 1 mostrará «Actualiza Iberia Smart Disc» en lugar de fallar.

Así cualquier disco vendido hoy seguirá funcionando con versiones futuras.

## 5. Contenido recomendado del disco

```text
D:\
├── iberia-disc.json     Obligatorio
├── cover.png            Carátula (recomendado, ~600 × 900 px)
├── icon.ico             Icono de la unidad
├── autorun.inf          Solo icono y nombre de la unidad en «Este equipo»
└── LEEME.txt            Qué es el disco y dónde descargar el programa
```

`autorun.inf` (ver [ejemplo](../examples/smart-disc/autorun.inf)) solo con
`icon=` y `label=`: Windows sigue mostrando el icono y el nombre de los discos
ópticos, pero hace años que no ejecuta nada desde ahí, e Iberia Smart Disc
tampoco lo usa. Es presentación: el lector aparece con la carátula del juego.

### 5.1. Discos con contenido (`data-disc`)

> Una ISO montada con doble clic cuenta como disco. Por eso el programa trata
> cualquier `data-disc` como de origen no verificado.

```json
{
  "iberiaDisc": true,
  "format": 1,
  "discType": "data-disc",
  "game": { "id": "mi-juego", "name": "Mi Juego" },
  "discLaunch": { "mode": "executable", "path": "Game\\Game.exe" }
}
```

- Solo se ejecuta el archivo de `discLaunch`; nunca otro `.exe` del disco.
- **Se pregunta cada vez** que se mete el disco, mostrando el archivo, la
  carpeta y los argumentos. No se recuerda: el manifiesto de un disco legítimo
  se puede copiar a otro con otro programa. Para abrir sin preguntar hará falta
  la firma de manifiestos prevista para la v0.3.
- Justo antes de abrir se comprueba que el disco sigue siendo el mismo.
- `"mode": "installer"` aparece como «Instalar desde el disco», se confirma
  siempre y no admite argumentos. Admite `.exe` y `.msi`.
- Solo contenido que se pueda distribuir legalmente (juegos DRM-free propios o
  licenciados, extras, manuales, bandas sonoras).

## 6. Generar el archivo desde otra aplicación

Quien genere `iberia-disc.json` automáticamente (por ejemplo, la tienda al
preparar un pedido) debe cumplir esto:

- **Lista blanca de campos.** Construir el objeto campo a campo, nunca
  copiando un registro entero ni con plantillas de texto: un nombre con
  comillas podría colar campos como `discLaunch`.
- **Sin datos privados.** El disco viaja con el cliente y su contenido queda en
  su PC. `discId` y `game.edition` no llevan nombres, correos, direcciones,
  tokens de acceso ni números correlativos.
- **Límites.** `game.id` y `discId` de 64 caracteres como máximo, también con
  el identificador más largo del catálogo. Comprobarlo al generar y dar un error
  claro.
- **Serialización.** `JSON.stringify(obj, null, 2)` o equivalente, UTF-8 sin BOM,
  menos de 64 KB.
- **Validación.** Validar contra una copia de `schema/iberia-disc.schema.json`
  fijada por versión del launcher, con un validador draft 2020-12 y sus opciones
  por defecto (en ajv: ni `useDefaults` ni `strict`). Pasar también el corpus
  `tests/fixtures/manifests`: los casos de `invalid-csharp-only` el esquema no
  los puede expresar.
- **Descarga.** `Content-Type: application/json; charset=utf-8`,
  `Content-Disposition: attachment; filename="iberia-disc.json"`,
  `Cache-Control: no-store` y `X-Content-Type-Options: nosniff`, solo para
  usuarios autorizados y sin efectos en GET.

## 7. Probar un disco antes de grabarlo

- En Iberia Smart Disc: **Configuración → Avanzado → Probar con una carpeta…**,
  o `IberiaSmartDisc.exe --test-disc "C:\ruta\a\la\carpeta"`.
- Para una prueba idéntica a un disco real, crear una ISO con
  [`tools/New-TestIso.ps1`](../tools/New-TestIso.ps1) y montarla con doble clic:
  Windows la trata como un lector de DVD.
