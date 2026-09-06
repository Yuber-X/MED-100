# BLOCKERS — MED-100

> Decisiones pendientes que requieren respuesta de Yuber (o del cliente final).

## #1 — ¿Dónde cambia su contraseña un usuario no-Admin?
La spec original ponía "Cambio de contraseña" dentro de Configuración para todos los roles,
pero la regla nueva (2026-07-11) hace Configuración exclusiva de Admin.
Opciones: (a) menú de usuario en el shell (icono arriba a la derecha → "Cambiar mi contraseña"),
(b) que solo Admin resetee contraseñas desde Admin de Usuarios.
**Propuesta: (a)** — es lo habitual y no carga al Admin. Decidir a más tardar en Fase 6.

## #2 — Nombre definitivo del producto — ✅ RESUELTO (2026-09-06)
El producto se llama **MediControl**. Yuber delegó la decisión ("decide tú, que tenga
que ver con clínica, sea entendible y pegajoso") y se eligió por tres razones: se
entiende de un golpe en español, es inequívocamente médico, y sigue la familia de
productos (PrestControl, DealerControl, AutoControl, FAControl), así que la marca
acumula en vez de dispersarse.

**Solo se renombró la capa visible** (títulos, sidebar, instalador, mensajes), a
través de `MED100.Common.AppInfo.Nombre`. Siguen intactos y NO deben tocarse:
`AppId` del instalador, `%ProgramData%\MED-100\licencia.dat`, los namespaces
`MED100.*` y la base `med100_db`. MED-100 es el nombre del PROYECTO; MediControl,
el del PRODUCTO.

Falta solo confirmarle el nombre al cliente antes de imprimir material.

## #3 — Mapeo del semáforo de caducidad (6 colores → 4)
El POS-400 usa 6 colores por meses restantes (≥9 verde ... ≤1 rojo). La spec pide 4.
Se mapeó conservando la lógica mensual: Verde ≥7m · Amarillo 4–6m · Naranja 2–3m · Rojo ≤1m
(incluye caducados). Corregible en `CalculadoraCaducidad` si el cliente quiere otros umbrales.
