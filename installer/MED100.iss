; =============================================================
; MED-100 — Instalador (Inno Setup 6)
; Compilar:  ISCC.exe MED100.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/MED100.App -c Release -r win-x64 --self-contained true -o publish
;
; PREREQUISITOS: si los instaladores de MySQL, AnyDesk y Google Drive estan en
; installer\prerequisitos\, el asistente ofrece instalarlos antes de abrir
; MED-100. Si NO estan, el instalador compila igual y esa pagina simplemente no
; aparece — asi el .iss sirve para armar el paquete completo o solo la app.
; Ver prerequisitos\LEEME.txt.
; =============================================================

; El nombre VISIBLE del producto (2026-09-06). "MED-100" era el provisional.
#define AppNombre "Odonto Unión"
#define AppVersion "1.3.0"

; ⚠ LA CARPETA DE DATOS NO CAMBIA CON EL NOMBRE. Ahi vive licencia.dat, el
; ancla que recuerda desde cuando corre el demo de 15 dias. Si se renombrara,
; toda instalacion ya activada empezaria la prueba de cero.
#define CarpetaDatos "MED-100"

; Bloquea la instalacion mientras la aplicacion este abierta. El mismo nombre
; que registra App.xaml.cs. Sin esto, Windows difiere los archivos en uso al
; proximo reinicio y el asistente termina diciendo "listo" sin haber cambiado
; nada — que es exactamente lo que le paso a FAControl el 2026-09-05.
#define AppMutexNombre "Global\MediControl.App.Instancia"
#define AppEditor "Yuber Santana"
#define AppExe "MED100.App.exe"
#define AppTelefono "849-438-0242"

; --- Prerequisitos: nombre esperado de cada instalador ---
#define DirPrereq "prerequisitos"
#define ExeAnyDesk "AnyDesk.exe"
; Es el instalador WEB de MySQL: pesa 2 MB y descarga lo demas al correr, asi
; que la PC del cliente necesita internet durante ESE paso.
#define ExeMySql "mysql-installer-community-8.0.46.0.msi"
#define ExeDrive "GoogleDriveSetup.exe"

; FileExists se evalua al COMPILAR: por eso el .iss sirve con y sin los archivos
#define TieneAnyDesk FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeAnyDesk)
#define TieneMySql   FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeMySql)
#define TieneDrive   FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeDrive)

[Setup]
AppId={{9A4C7D31-2E68-4B15-A0F9-MED100SYSTEM}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppMutex={#AppMutexNombre}
AppPublisher={#AppEditor}
AppSupportPhone={#AppTelefono}
DefaultDirName={autopf}\{#AppNombre}
DefaultGroupName={#AppNombre}
OutputDir=Output
OutputBaseFilename=OdontoUnion_Setup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\MED100.App\Assets\med100.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"

; --- Prerequisitos: una casilla por programa, solo si el instalador esta presente ---
#if TieneMySql
Name: "prereq_mysql"; Description: "Instalar MySQL Server (la base de datos de {#AppNombre})"; \
  GroupDescription: "Programas necesarios:"
#endif
#if TieneAnyDesk
Name: "prereq_anydesk"; Description: "Instalar AnyDesk (para dar soporte a distancia)"; \
  GroupDescription: "Programas necesarios:"
#endif
#if TieneDrive
Name: "prereq_drive"; Description: "Instalar Google Drive (para subir los respaldos a la nube)"; \
  GroupDescription: "Programas necesarios:"
#endif

[Files]
; Aplicación publicada (self-contained: no requiere instalar .NET)
Source: "..\publish\*"; DestDir: "{app}"; \
  Excludes: "MED100.App.dll.config"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
; La configuración (cadena de conexión) NUNCA se pisa en actualizaciones
Source: "..\publish\MED100.App.dll.config"; DestDir: "{app}"; \
  Flags: onlyifdoesntexist uninsneveruninstall
; Scripts de base de datos y documentacion.
; Por PATRON y no uno por uno: agregar una migracion nueva no deberia obligar a
; acordarse de tocar el instalador. Se excluye 999_rollback.sql a proposito:
; BORRA la base entera y no tiene nada que hacer en la maquina del cliente.
Source: "..\scripts\db\*.sql"; DestDir: "{app}\scripts\db"; \
  Excludes: "999_rollback.sql"; \
  Flags: ignoreversion
Source: "..\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

; --- Prerequisitos: van a la carpeta temporal y se borran al terminar ---
#if TieneMySql
Source: "{#DirPrereq}\{#ExeMySql}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_mysql
#endif
#if TieneAnyDesk
Source: "{#DirPrereq}\{#ExeAnyDesk}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_anydesk
#endif
#if TieneDrive
Source: "{#DirPrereq}\{#ExeDrive}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_drive
#endif

[Dirs]
; La app escribe logs\ y ajustes.json junto al ejecutable:
; los usuarios estándar necesitan permiso de modificación
Name: "{app}"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify
; El ancla de la licencia (la segunda copia de la fecha de instalación) vive
; acá y NO junto al .exe: desinstalar no debe reiniciar el demo. Va con
; users-modify porque la escribe quien abra la app — la recepcionista de la
; mañana y la de la tarde suelen ser cuentas de Windows distintas.
Name: "{commonappdata}\{#CarpetaDatos}"; Permissions: users-modify; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#AppNombre}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Manual de usuario"; Filename: "{app}\docs\MANUAL.md"
Name: "{autodesktop}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; Tasks: escritorio

[Run]
; ---- Primero los prerequisitos, DESPUES la app ----
; Van con la interfaz VISIBLE a proposito: MySQL pide la contrasena de root y
; hay que elegirla con el cliente delante, no dejarla al azar ni escondida en
; un instalador silencioso que despues nadie sabe que puso.
#if TieneMySql
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#ExeMySql}"""; \
  StatusMsg: "Instalando MySQL Server…"; Tasks: prereq_mysql
#endif
#if TieneAnyDesk
Filename: "{tmp}\{#ExeAnyDesk}"; \
  StatusMsg: "Instalando AnyDesk…"; Tasks: prereq_anydesk
#endif
#if TieneDrive
Filename: "{tmp}\{#ExeDrive}"; \
  StatusMsg: "Instalando Google Drive…"; Tasks: prereq_drive
#endif

Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Los logs se van con la app; ajustes.json y la BD (MySQL) se conservan
Type: filesandordirs; Name: "{app}\logs"

[Code]
{ ---------------------------------------------------------------
  MED-100 necesita MySQL. Antes de instalar comprobamos que exista
  el servicio; si no está, avisamos con el enlace de descarga en vez
  de dejar que el usuario descubra el problema al abrir la app.
  La BASE DE DATOS no hace falta crearla a mano: MED-100 la crea sola
  en el primer arranque (esquema + roles + permisos).
  --------------------------------------------------------------- }

function ServicioMySqlInstalado(): Boolean;
var
  Salida: AnsiString;
  Temporal: String;
  Codigo: Integer;
begin
  Result := False;
  Temporal := ExpandConstant('{tmp}\mysql_check.txt');
  { 'sc query' devuelve 0 si el servicio existe }
  if Exec(ExpandConstant('{cmd}'), '/C sc query MySQL80 > "' + Temporal + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, Codigo) then
  begin
    if Codigo = 0 then
      Result := True
    else
    begin
      { Algunas instalaciones usan otro nombre de servicio }
      if Exec(ExpandConstant('{cmd}'),
              '/C sc query state= all | findstr /I "MySQL" > "' + Temporal + '"',
              '', SW_HIDE, ewWaitUntilTerminated, Codigo) then
        Result := (Codigo = 0);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Respuesta: Integer;
begin
  Result := True;
  if not ServicioMySqlInstalado() then
  begin
    Respuesta := MsgBox(
      'MED-100 guarda las ventas en MySQL, y no encontramos MySQL instalado en este equipo.' + #13#10 + #13#10 +
      'Instala primero "MySQL Community Server 8.x" (opción "Server only") desde:' + #13#10 +
      '   https://dev.mysql.com/downloads/installer/' + #13#10 + #13#10 +
      'La base de datos NO hace falta crearla a mano: MED-100 la crea sola la primera vez que se abre.' + #13#10 + #13#10 +
      '¿Continuar igual con la instalación?',
      mbConfirmation, MB_YESNO);
    Result := (Respuesta = IDYES);
  end;
end;
