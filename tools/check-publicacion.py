"""Comprueba que lo que se va a publicar no filtra nada: caracteres invisibles o
de dirección (bidi), rutas locales, correos, claves y tokens, y las cadenas de
una lista negra opcional que no se publica (por ejemplo, datos internos de la
tienda).

Uso:
    python tools/check-publicacion.py [--denylist ARCHIVO] [RAÍZ]

Revisa los archivos que se publicarían: con git, los que no ignora .gitignore;
sin git, todos salvo bin/, obj/, dist/ y TestResults/. Sale con código 1 si
encuentra algo. Nunca muestra el valor encontrado entero.
"""
import argparse
import os
import pathlib
import re
import subprocess
import sys

SKIPPED_DIRS = {".git", "bin", "obj", "dist", "TestResults", ".vs", ".idea", ".vscode"}

# Mismos rangos que src/IberiaSmartDisc.Core/Common/TextSafety.cs, salvo los
# controles de texto normales (tabulador, saltos de línea).
INVISIBLE_RANGES = [
    (0x0000, 0x0008), (0x000B, 0x000C), (0x000E, 0x001F), (0x007F, 0x009F), (0x00AD, 0x00AD),
    (0x0600, 0x0605), (0x061C, 0x061C), (0x06DD, 0x06DD), (0x070F, 0x070F), (0x08E2, 0x08E2),
    (0x180E, 0x180E), (0x200B, 0x200F), (0x2028, 0x202E), (0x2060, 0x206F), (0xE000, 0xF8FF),
    (0xFEFF, 0xFEFF), (0xFFF9, 0xFFFB), (0xE0000, 0x10FFFF),
]
INVISIBLE = re.compile("[" + "".join(f"{chr(a)}-{chr(b)}" for a, b in INVISIBLE_RANGES) + "]")

LOCAL_PATH = re.compile(r"(?:/(?:root|home/[a-z_][a-z0-9_-]*|Users/[A-Za-z][^/\s]*)/|[A-Za-z]:\\Users\\(?![<%])[^\\\s]+\\)")
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")
SECRETS = [
    ("clave privada", re.compile("-----BEGIN" + r"(?: [A-Z0-9]+)* PRIVATE KEY-----")),
    ("token de GitHub", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})\b")),
    ("clave de AWS", re.compile(r"\bAKIA[0-9A-Z]{16}\b")),
    ("clave de API", re.compile(r"\b(?:sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{35}|xox[baprs]-[A-Za-z0-9-]{10,})\b")),
    ("asignación de secreto", re.compile(r"(?i)\b(?:password|passwd|secret|api[_-]?key|token)\s*[:=]\s*[\"'][^\"'\s]{8,}[\"']")),
]
# Correos que pueden aparecer a propósito (ninguno por ahora).
ALLOWED_EMAILS: set = set()


def publishable_files(root: pathlib.Path):
    try:
        out = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-co", "--exclude-standard", "-z"],
            check=True, capture_output=True,
        ).stdout
        return [root / p for p in out.decode("utf-8").split("\0") if p]
    except (OSError, subprocess.CalledProcessError):
        files = []
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in SKIPPED_DIRS]
            files.extend(pathlib.Path(dirpath) / f for f in filenames)
        return files


def redact(value: str) -> str:
    return value if len(value) <= 6 else value[:3] + "…" + value[-2:]


def check_text(rel: str, text: str, denylist):
    problems = []
    for number, line in enumerate(text.splitlines(), 1):
        for match in INVISIBLE.finditer(line):
            # Un BOM al principio del archivo es solo la marca de codificación (.sln, .ps1).
            if ord(match.group()) == 0xFEFF and number == 1 and match.start() == 0:
                continue
            problems.append(f"{rel}:{number}: carácter invisible o bidi U+{ord(match.group()):04X}")
        for match in LOCAL_PATH.finditer(line):
            problems.append(f"{rel}:{number}: ruta local ({redact(match.group())})")
        for match in EMAIL.finditer(line):
            if match.group().lower() not in ALLOWED_EMAILS:
                problems.append(f"{rel}:{number}: correo ({redact(match.group())})")
        for label, pattern in SECRETS:
            if pattern.search(line):
                problems.append(f"{rel}:{number}: posible {label}")
        lower = line.lower()
        for entry in denylist:
            if entry in lower:
                problems.append(f"{rel}:{number}: cadena de la lista negra ({redact(entry)})")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--denylist", type=pathlib.Path, help="archivo con una cadena prohibida por línea (# comenta)")
    parser.add_argument("root", nargs="?", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parent.parent)
    args = parser.parse_args()

    denylist = []
    if args.denylist:
        for raw in args.denylist.read_text(encoding="utf-8").splitlines():
            entry = raw.strip()
            if entry and not entry.startswith("#"):
                denylist.append(entry.lower())

    root = args.root.resolve()
    problems = []
    files = publishable_files(root)
    for path in files:
        rel = path.relative_to(root).as_posix()
        try:
            data = path.read_bytes()
        except OSError as error:
            problems.append(f"{rel}: no se pudo leer ({error.strerror})")
            continue
        if b"\0" in data[:8192]:
            # Binarios (iconos, imágenes): solo cadenas legibles de la lista negra, correos y rutas.
            text = data.decode("latin-1")
            for entry in denylist + [m.group() for m in EMAIL.finditer(text)] + [m.group() for m in LOCAL_PATH.finditer(text)]:
                if entry and entry.lower() in text.lower():
                    problems.append(f"{rel}: binario con texto sospechoso ({redact(entry)})")
            continue
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            problems.append(f"{rel}: no es UTF-8")
            continue
        problems.extend(check_text(rel, text, denylist))

    if problems:
        print("\n".join(sorted(set(problems))))
        print(f"\n{len(set(problems))} problema(s) en {len(files)} archivos. No publiques hasta resolverlos.")
        return 1
    extra = f" y {len(denylist)} cadenas de la lista negra" if denylist else ""
    print(f"Sin fugas en {len(files)} archivos (comprobaciones genéricas{extra}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
