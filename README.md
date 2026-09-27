# MED-100

Punto de venta y gestor médico. De cara al cliente el producto se llama
**Odonto Unión** (ver `CLAUDE.md` §1).

## Correr desde el código

La cadena de conexión del repositorio viaja con valores de ejemplo a propósito
—`CAMBIAR_USUARIO` / `CAMBIAR_PASSWORD` en `src/MED100.App/App.config`—, porque
`App.config` está versionado y las credenciales no se suben nunca. Tal cual
viene, la aplicación abre y avisa que MySQL rechazó el usuario.

Para correrla en la máquina de desarrollo, **sin tocar ningún archivo
versionado**, se define la variable de entorno `MED100_CONEXION` con la cadena
completa:

```powershell
# Solo para esta ventana de PowerShell
$env:MED100_CONEXION = "Server=localhost;Port=3306;Database=med100_db;Uid=root;Pwd=root;"
dotnet run --project src/MED100.App
```

```powershell
# Fija, para todas las ventanas de aquí en adelante (se aplica al abrir una nueva)
setx MED100_CONEXION "Server=localhost;Port=3306;Database=med100_db;Uid=root;Pwd=root;"
```

Si la variable no está, se usa la cadena de `App.config`. **En la máquina del
cliente la variable no existe**: ahí la cadena la escribe el instalador en
`MED100.App.dll.config`, con el usuario dedicado `med100` (nunca root). Ver
`docs/INSTALL.md`.

La base se crea sola: si `med100_db` no existe, la aplicación la ofrece crear al
arrancar, y en cada arranque aplica los parches de esquema pendientes.

## Verificaciones antes de entregar

```powershell
dotnet build MED100.sln -c Release      # sin warnings
dotnet test                              # servicios + datos (los de datos necesitan MySQL)
python scripts/verificar_recursos_xaml.py
python scripts/verificar_bindings_solo_lectura.py
dotnet run --project scripts/verificar_desborde
```

## Documentación

| Archivo | Qué tiene |
|---|---|
| `CLAUDE.md` | Alcance, arquitectura y reglas del proyecto. **Leerlo primero.** |
| `docs/DESIGN.md` | Lenguaje visual |
| `docs/MANUAL.md` | Manual del usuario final |
| `docs/INSTALL.md` | Instalación en la clínica |
| `docs/ITBIS.md` | La matemática fiscal |
| `CHANGELOG.md` | Qué cambió en cada versión |
| `TODO.md` | Lo que falta y las pruebas manuales pendientes |
| `BLOCKERS.md` | Decisiones trabadas esperando al cliente |
