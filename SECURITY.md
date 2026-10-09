# Seguridad

## Versiones con soporte

Solo la última versión publicada en
[Releases](https://github.com/Davidvx98/iberia-smart-disc/releases/latest).

## Cómo avisar de una vulnerabilidad

No abras un *issue* público. Usa **Security → Report a vulnerability** en este
repositorio (aviso privado de GitHub).

Intentaremos responder en una semana y publicar la corrección en 30 días, o
antes si se está explotando. Te citaremos en las notas de la versión si quieres.

## Qué entra

- Que un disco, una ISO o un `iberia-disc.json` ejecute algo sin que el usuario
  lo acepte, o algo distinto de lo que aceptó.
- Errores del lector del manifiesto: rutas que salen del disco, textos que
  engañan en los diálogos, consumo excesivo de memoria o CPU.
- Instalación, actualización o desinstalación que permitan cargar código ajeno
  (DLL, archivos junto al `.exe`), escalar privilegios o borrar lo que no deben.
- La cadena de publicación: workflows, artefactos y releases.

## Qué no entra

- Lo que requiere ya poder ejecutar código como el mismo usuario y con la misma
  integridad, salvo que el programa le dé algo que no tenía (por ejemplo,
  saltarse el aviso al abrir un programa del disco).
- Que Windows SmartScreen o Smart App Control avisen de un ejecutable sin firmar.
- Juegos o plataformas de terceros (Steam, Epic, GOG).

## Cómo verificar una descarga

Descarga solo desde Releases de este repositorio o desde la web
<https://iberiacustomdvds.es/launcher>. Antes de abrirla:

```powershell
Get-FileHash .\IberiaSmartDisc.exe -Algorithm SHA256
```

El resultado debe coincidir con `IberiaSmartDisc.exe.sha256` de la misma
versión. Con la CLI de GitHub puedes comprobar además que el archivo lo compiló
este repositorio:

```bash
gh attestation verify IberiaSmartDisc.exe -R Davidvx98/iberia-smart-disc
```
