# BLOCKERS — MED-100

> Decisiones pendientes que requieren respuesta de Yuber (o del cliente final).

## #1 — ¿Dónde cambia su contraseña un usuario no-Admin?
La spec original ponía "Cambio de contraseña" dentro de Configuración para todos los roles,
pero la regla nueva (2026-07-11) hace Configuración exclusiva de Admin.
Opciones: (a) menú de usuario en el shell (icono arriba a la derecha → "Cambiar mi contraseña"),
(b) que solo Admin resetee contraseñas desde Admin de Usuarios.
**Propuesta: (a)** — es lo habitual y no carga al Admin. Decidir a más tardar en Fase 6.

## #2 — Nombre definitivo del producto
"MED-100" es provisional según la spec. El .sln, namespaces e instalador usan MED100;
renombrar después es barato a nivel de UI (título, ticket) pero caro a nivel de namespaces.
Confirmar antes de la Fase 7 (empaquetado).

## #3 — Mapeo del semáforo de caducidad (6 colores → 4)
El POS-400 usa 6 colores por meses restantes (≥9 verde ... ≤1 rojo). La spec pide 4.
Se mapeó conservando la lógica mensual: Verde ≥7m · Amarillo 4–6m · Naranja 2–3m · Rojo ≤1m
(incluye caducados). Corregible en `CalculadoraCaducidad` si el cliente quiere otros umbrales.
