"""
Verifica que toda clave de recurso usada en los XAML esté definida en alguno.

POR QUÉ EXISTE
--------------
WPF resuelve StaticResource/DynamicResource en TIEMPO DE EJECUCIÓN. Un
`{DynamicResource Brush.NoExiste}` compila sin una sola advertencia y revienta
recién cuando alguien abre esa pantalla — en la clínica, con un paciente
delante. Ya pasó dos veces en este proyecto:

  · 2026-08-12: un ElementStyle apuntando a un estilo de DataGridCell tiró
    quince diálogos de error apilados al entrar a Médicos;
  · 2026-08-14: un BasedOn="{StaticResource {x:Type TextBlock}}" en la ficha
    del paciente, que no tiene estilo implícito definido.

Correrlo antes de entregar cuesta un segundo.

USO
---
    python scripts/verificar_recursos_xaml.py

Devuelve 0 si está todo bien, 1 si falta alguna clave.

LO QUE NO CUBRE
---------------
Solo mira claves con nombre. No detecta un `{x:Type Algo}` sin estilo
implícito ni un TargetType que no case con el elemento — para eso hay que
abrir la pantalla.
"""
import glob
import io
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def archivos_xaml():
    patron = os.path.join(RAIZ, "src", "**", "*.xaml")
    for ruta in glob.glob(patron, recursive=True):
        partes = ruta.replace("\\", "/").split("/")
        if "bin" in partes or "obj" in partes:
            continue
        yield ruta


def main():
    rutas = list(archivos_xaml())
    if not rutas:
        print("No se encontró ningún XAML bajo src/.")
        return 1

    definidas = set()
    for ruta in rutas:
        contenido = io.open(ruta, encoding="utf-8-sig").read()
        definidas.update(re.findall(r'x:Key="([^"]+)"', contenido))

    usadas = {}
    for ruta in rutas:
        contenido = io.open(ruta, encoding="utf-8-sig").read()
        for m in re.finditer(r"\{(?:Static|Dynamic)Resource\s+([^\}\s]+)\s*\}", contenido):
            clave = m.group(1)
            # {StaticResource {x:Type Boton}} se salta: es un estilo implícito,
            # y esos hay que comprobarlos abriendo la pantalla.
            if clave.startswith("{x:Type"):
                continue
            usadas.setdefault(clave, set()).add(os.path.relpath(ruta, RAIZ))

    faltan = {k: v for k, v in usadas.items() if k not in definidas}

    if not faltan:
        print("OK — las %d claves usadas están definidas (%d definidas en total)."
              % (len(usadas), len(definidas)))
        return 0

    print("FALTAN %d clave(s) de recurso:\n" % len(faltan))
    for clave in sorted(faltan):
        print("  %-40s usada en %s" % (clave, ", ".join(sorted(faltan[clave]))))
    print("\nEstas NO fallan al compilar: revientan al abrir la pantalla.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
