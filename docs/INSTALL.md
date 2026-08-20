# INSTALL.md — Guía de instalación de MED-100

> Para quien instala el sistema en la PC del cliente (Yuber o un técnico).
> Tiempo estimado: 20–30 minutos. Windows 10 (build 19041) u 11, 64 bits.

---

## 1. Requisitos

| Componente | Versión | Nota |
|---|---|---|
| Windows | 10 (build 19041) o 11, x64 | |
| MySQL Community Server | 8.0 o superior | Única dependencia externa |
| .NET | — | NO hace falta: el instalador lo incluye todo |

---

## 2. Instalar MySQL Server

1. Descargar **MySQL Community Server 8.x**: https://dev.mysql.com/downloads/installer/
2. En el instalador elegir **Server only**.
3. Configuración:
   - Config Type: **Development Computer** (consume menos RAM)
   - Puerto: **3306** (por defecto)
   - Authentication: **Use Strong Password Encryption**
   - Root password: elegir una y **guardarla en un lugar seguro**
   - Windows Service: dejar **MySQL80**, arranque automático ✔
4. Verificar que el servicio corre: `services.msc` → MySQL80 → *En ejecución*.

> El instalador de MED-100 avisa si no encuentra MySQL, pero no lo instala por ti.

## 3. Instalar MED-100

1. Ejecutar **`MED100_Setup_x.x.x.exe`** y seguir el asistente.
2. Se instala en `C:\Program Files\MED-100` con acceso directo en el escritorio.

## 4. La base de datos se crea SOLA

**No hace falta ejecutar ningún script.** Al abrir MED-100 por primera vez:

1. La app detecta que la base de datos no existe y pregunta:
   *"¿Quieres crearla ahora?"* → **Sí**.
2. Crea `med100_db` con todas las tablas, los 4 roles (Admin, Supervisor, Cajero,
   Vendedor) y sus permisos.
3. Aparece el asistente **"Crear cuenta inicial"**: el cliente elige su usuario, nombre y
   una contraseña de mínimo 8 caracteres. **Esa cuenta es el Administrador** — anotarla.

> Para que la app pueda crear la base de datos, la cadena de conexión debe usar un usuario
> con permiso de creación (root sirve). Ver el paso 5 para dejarlo seguro después.

Si prefieres crearla a mano (o el usuario configurado no puede):
```sql
SOURCE C:/Program Files/MED-100/scripts/db/001_create_schema.sql;
SOURCE C:/Program Files/MED-100/scripts/db/002_seed_data.sql;
```
Ejecutar el cliente con `mysql -uroot -p --default-character-set=utf8mb4` para que los
acentos ("Almacén", "Configuración") se guarden bien.

## 5. Configurar la conexión (recomendado para producción)

Por seguridad, la app no debería correr como root:

1. Abrir `scripts\db\003_crear_usuario_dedicado.sql`, reemplazar `CAMBIAR-ESTA-CLAVE`
   por una contraseña real y ejecutarlo como root.
2. Editar (como administrador) `C:\Program Files\MED-100\MED100.App.dll.config`:

```xml
<connectionStrings>
  <add name="MED100Db"
       connectionString="Server=localhost;Port=3306;Database=med100_db;Uid=med100;Pwd=LA-CLAVE-DEL-PASO-1;" />
</connectionStrings>
```

> Hazlo **después** del primer arranque: el usuario dedicado no puede crear la base de datos.

## 5.b Licencia: prueba de 15 días o llave del producto

MED-100 se instala en **modo de prueba** y funciona **completo durante 15 días**, contados
desde el primer arranque. No hay nada que hacer para empezar la prueba.

- Mientras dure, en el menú lateral se ve una pastilla **DEMO · N días**.
- En los **últimos 5 días**, al abrir la aplicación aparece la ventana de activación. Se
  puede seguir probando con el botón *Seguir en modo de prueba*.
- **Al vencer, la aplicación no abre**: solo aparece la ventana de activación. Los datos
  quedan intactos — escribir la llave devuelve todo tal cual estaba.

Para activar: **Configuración → Licencia → Activar**, y escribir la llave del producto
(formato `MED1-XXXXX-XXXXX-XXXXX`). Se activa una sola vez por instalación; después no se
vuelve a pedir.

> La llave está en el documento de códigos y accesos, **no** en esta guía: esta guía se
> le entrega al cliente.

**Detalles técnicos**

- La fecha de instalación se guarda en la tabla `licencia` de la base y, cifrada, en
  `%ProgramData%\MED-100\licencia.dat`. Manda **la más vieja de las dos**: borrar la base
  o desinstalar no reinicia los 15 días.
- Si se atrasa el reloj de Windows más de 24 horas, la aplicación pide activación
  (`FECHA ALTERADA`). Poner la fecha correcta lo resuelve.
- Al **migrar a otra PC** (sección 7) la prueba empieza de cero en la PC nueva, porque el
  ancla es de ese equipo. Si la copia estaba activada, hay que **volver a escribir la
  llave** en la PC nueva.
- Actualizar desde 1.0.x no requiere hacer nada: la tabla `licencia` se crea sola al
  arrancar. Si el arranque avisa que no pudo crearla, ejecutar como root
  `scripts\db\008_licencia.sql`.

## 6. Después de instalar (checklist)

- [ ] Entrar como Admin y **crear los usuarios** de los empleados (Configuración no, sino **Usuarios**):
      elegir su rol y ajustar sus permisos (por ejemplo, quitarle a un cajero editar productos)
- [ ] Configuración → **Datos del negocio**: nombre, RNC (opcional), dirección y teléfono
      (salen impresos en cada factura)
- [ ] Configuración → **Cálculos e impuestos**: confirmar el ITBIS (18%) o desactivarlo si no aplica
- [ ] Configuración → **Respaldar ahora** → guardar el primer respaldo y verificar que el .sql se creó
- [ ] Configuración → activar el **export automático a Excel** si el cliente lo quiere
- [ ] Configuración → **tamaño de texto** cómodo para el cliente
- [ ] Configuración → **cierre de caja automático** si quiere que la caja se cierre sola a cierta hora
- [ ] Cargar los **productos** (con su código de barras, si lo tienen)
- [ ] Si la clínica ya compró: Configuración → **Licencia** → activar con la llave (sección 5.b)
- [ ] Entregar el **MANUAL.md** al cliente
- [ ] Acordar rutina de respaldo (recomendado: semanal, a USB o nube)

## 7. Migrar a otra PC

1. En la PC vieja: Configuración → **Respaldar ahora** → guardar el `.sql` en un USB.
2. En la PC nueva: seguir esta guía (pasos 2–5).
3. Configuración → **Restaurar desde archivo…** → elegir el `.sql` del USB.
4. Listo: ventas, numeración de facturas, productos, clientes, usuarios e historial quedan idénticos.

## 8. Problemas comunes

| Síntoma | Causa probable | Solución |
|---|---|---|
| "No se pudo conectar con MySQL" al abrir | Servicio MySQL80 detenido | `services.msc` → iniciar MySQL80 |
| "MySQL rechazó el usuario o la contraseña" | Contraseña del App.config no coincide | Revisar paso 5 |
| La app pide crear la BD y falla | El usuario configurado no puede crear bases | Usar root la primera vez, o ejecutar 001+002 a mano |
| Los acentos se ven raros ("Almac├®n") | Los scripts se corrieron sin UTF-8 | Ejecutar `scripts\db\004_reparar_acentos.sql` |
| Respaldo falla: "no se encontró mysqldump" | MySQL instalado sin agregarlo al PATH | La app lo busca sola en `Program Files\MySQL`; verificar que exista `bin\mysqldump.exe` |
| El menú no muestra todos los módulos | El usuario no tiene esos permisos | Entrar como Admin → Usuarios → ajustar sus permisos |
| Ventana de login cortada | Resolución muy baja | Mínimo 1280×720 |

---
*MED-100 · desarrollado por Yuber Santana · soporte según contrato de mantenimiento.*
