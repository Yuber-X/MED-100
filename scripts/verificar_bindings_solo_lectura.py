"""
Busca bindings que WPF liga TwoWay POR DEFECTO apuntando a propiedades de solo
lectura.

POR QUÉ EXISTE
--------------
Algunas propiedades de WPF se ligan TwoWay aunque el XAML no lo diga:
`TextBox.Text`, `ComboBox.SelectedItem`, `CheckBox.IsChecked`,
`DatePicker.SelectedDate` y —la que costó el 2026-09-26— `DisplayDateStart`.
Si la propiedad del ViewModel es de solo lectura (`=>` o `{ get; }`), WPF tira
al aplicar el binding:

    A TwoWay or OneWayToSource binding cannot work on the read-only
    property 'PrimerDiaAgendable' of type 'MED100.ViewModels.CitasViewModel'.

Compila sin una advertencia y revienta recién cuando alguien abre esa pantalla
—en la clínica, con un paciente delante—. Le pasó al cliente al abrir "Nueva
cita".

El arreglo es siempre el mismo: `Mode=OneWay` explícito en ese binding.

USO
---
    python scripts/verificar_bindings_solo_lectura.py

Devuelve 0 si está todo bien, 1 si encuentra alguno.

LO QUE NO CUBRE
---------------
* Rutas con punto (`Seleccionada.Nombre`): no se resuelve a qué tipo llegan.
* Bindings armados desde C#.
* Las columnas de DataGrid: su binding también es TwoWay por defecto, pero con
  la grilla en solo lectura WPF nunca intenta escribir y no tira.
"""
import glob
import io
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# (elemento, atributo) que WPF liga TwoWay por defecto.
RIESGOSOS = {
    "TextBox": {"Text"},
    "RichTextBox": {"Text"},
    "PasswordBox": {"Password"},
    "ComboBox": {"SelectedItem", "SelectedValue", "SelectedIndex", "Text"},
    "ListBox": {"SelectedItem", "SelectedValue", "SelectedIndex"},
    "ListView": {"SelectedItem", "SelectedValue", "SelectedIndex"},
    "TabControl": {"SelectedItem", "SelectedValue", "SelectedIndex"},
    "DataGrid": {"SelectedItem", "SelectedValue", "SelectedIndex"},
    "CheckBox": {"IsChecked"},
    "RadioButton": {"IsChecked"},
    "ToggleButton": {"IsChecked"},
    "Expander": {"IsExpanded"},
    "Slider": {"Value"},
    "DatePicker": {"SelectedDate", "DisplayDate", "DisplayDateStart", "DisplayDateEnd"},
    "Calendar": {"SelectedDate", "DisplayDate", "DisplayDateStart", "DisplayDateEnd"},
}

ELEMENTO = re.compile(r"<(\w+)((?:\s+[^<>]*?)?)/?>", re.DOTALL)
ATRIBUTO = re.compile(r'(\w+)\s*=\s*"(\{[^"]*\}|[^"]*)"', re.DOTALL)
RUTA_SIMPLE = re.compile(r"^\s*(?:Path\s*=\s*)?([A-Za-z_]\w*)\s*(?:,|\}|$)")


def propiedades_de_solo_lectura():
    """Nombre de propiedad -> True si en TODAS las clases donde aparece es de solo lectura."""
    settable = set()
    solo_lectura = set()

    patrones = [
        os.path.join(RAIZ, "src", "MED100.ViewModels", "**", "*.cs"),
        os.path.join(RAIZ, "src", "MED100.Models", "**", "*.cs"),
        os.path.join(RAIZ, "src", "MED100.Common", "**", "*.cs"),
    ]
    for patron in patrones:
        for archivo in glob.glob(patron, recursive=True):
            if os.sep + "obj" + os.sep in archivo or os.sep + "bin" + os.sep in archivo:
                continue
            texto = io.open(archivo, encoding="utf-8-sig").read()

            # [ObservableProperty] private T _campo;      -> Campo, con setter
            # [ObservableProperty] private T _campo = "x"; -> idem. El
            # inicializador es justo lo que hacía fallar la primera versión de
            # esta expresión, y el falso positivo se cobró dos bindings sanos.
            for campo in re.findall(
                    r"\[ObservableProperty\][\s\S]{0,200}?private\s+[\w<>?\[\],\s]+?\s_(\w+)\s*(?:=|;)",
                    texto):
                settable.add(campo[0].upper() + campo[1:])

            # public T Nombre { get; set; }
            for nombre in re.findall(r"public\s+[\w<>?\[\],\s]+?\s(\w+)\s*\{\s*get;\s*set;", texto):
                settable.add(nombre)

            # public T Nombre { get; } / { get; init; } / { get; private set; }
            for nombre in re.findall(
                r"public\s+[\w<>?\[\],\s]+?\s(\w+)\s*\{\s*get;\s*(?:init;|private\s+set;|\})", texto):
                solo_lectura.add(nombre)

            # public T Nombre => ...
            for nombre in re.findall(r"public\s+[\w<>?\[\],\s]+?\s(\w+)\s*=>", texto):
                solo_lectura.add(nombre)

            # record Algo(T Uno, T Dos): posicionales, init-only
            for cuerpo in re.findall(r"record\s+\w+\s*\(([^)]*)\)", texto, re.DOTALL):
                for parametro in cuerpo.split(","):
                    partes = parametro.strip().split()
                    if len(partes) >= 2 and re.fullmatch(r"\w+", partes[-1]):
                        solo_lectura.add(partes[-1])

    return {n for n in solo_lectura if n not in settable}


def archivos_xaml():
    patron = os.path.join(RAIZ, "src", "**", "*.xaml")
    return [a for a in glob.glob(patron, recursive=True)
            if os.sep + "obj" + os.sep not in a and os.sep + "bin" + os.sep not in a]


def main():
    lectura = propiedades_de_solo_lectura()
    hallazgos = []

    for archivo in archivos_xaml():
        texto = io.open(archivo, encoding="utf-8-sig").read()
        for elemento in ELEMENTO.finditer(texto):
            tag = elemento.group(1)
            if tag not in RIESGOSOS:
                continue

            linea = texto.count("\n", 0, elemento.start()) + 1
            for atributo in ATRIBUTO.finditer(elemento.group(2) or ""):
                nombre, valor = atributo.group(1), atributo.group(2)
                if nombre not in RIESGOSOS[tag] or not valor.startswith("{Binding"):
                    continue
                if re.search(r"Mode\s*=\s*(OneWay|OneTime)\b", valor):
                    continue

                cuerpo = valor[len("{Binding"):]
                ruta = RUTA_SIMPLE.match(cuerpo)
                if not ruta:
                    continue
                propiedad = ruta.group(1)
                if propiedad in lectura:
                    hallazgos.append(
                        f"{os.path.relpath(archivo, RAIZ)}:{linea}  "
                        f"{tag}.{nombre} -> {propiedad} (solo lectura)")

    if not hallazgos:
        print("OK - ningun binding TwoWay apunta a una propiedad de solo lectura.")
        return 0

    print("BINDINGS QUE VAN A REVENTAR AL ABRIR LA PANTALLA:")
    for hallazgo in hallazgos:
        print("  " + hallazgo)
    print()
    print("Arreglo: agregar Mode=OneWay a esos bindings.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
