"""Comprueba que schema/iberia-disc.schema.json da el mismo veredicto que el
validador del programa sobre tests/fixtures/manifests (el lado C# lo comprueba
ManifestCorpusTests). Uso: python tools/check-schema.py (requiere jsonschema)."""
import json
import pathlib
import sys

from jsonschema import Draft202012Validator

root = pathlib.Path(__file__).resolve().parent.parent
schema = json.loads((root / "schema" / "iberia-disc.schema.json").read_text(encoding="utf-8"))
Draft202012Validator.check_schema(schema)
validator = Draft202012Validator(schema)
fixtures = root / "tests" / "fixtures" / "manifests"

problems = []
for expected, folder in (("válido", "valid"), ("inválido", "invalid")):
    for path in sorted((fixtures / folder).glob("*.json")):
        errors = list(validator.iter_errors(json.loads(path.read_text(encoding="utf-8"))))
        if (expected == "válido") == bool(errors):
            detail = "; ".join(e.message[:120] for e in errors[:2])
            problems.append(f"{folder}/{path.name}: el esquema no lo considera {expected} {detail}")

skipped = len(list((fixtures / "invalid-csharp-only").glob("*.json")))
if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"Esquema coherente con el corpus ({skipped} casos solo los rechaza el programa, por diseño).")
