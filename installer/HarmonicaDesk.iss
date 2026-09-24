#ifndef PublishDir
  #error PublishDir is required. Use scripts/Build-Desktop.ps1.
#endif
#ifndef OutputDir
  #error OutputDir is required. Use scripts/Build-Desktop.ps1.
#endif
#ifndef FilesInclude
  #error FilesInclude is required. The publication checker creates the explicit file list.
#endif
#ifndef RepoDir
  #error RepoDir is required.
#endif

#ifndef AppVersion
  #error AppVersion is required. Use scripts/Build-Desktop.ps1.
#endif
#define AppExe "HarmonicaDesk.exe"

[Setup]
AppId={{C0C404E7-1E8C-4F17-9378-7A046A728F36}
AppName=口琴小曲库
AppVersion={#AppVersion}
AppVerName=口琴小曲库 {#AppVersion}
AppPublisher=Atetra2007
AppPublisherURL=https://github.com/Atetra2007/delta-force-harmonica-kit
AppSupportURL=https://github.com/Atetra2007/delta-force-harmonica-kit/issues
DefaultDirName={localappdata}\Programs\HarmonicaDesk
DisableDirPage=yes
DefaultGroupName=口琴小曲库
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
AppMutex=Local\HarmonicaDesk.App
SetupMutex=Local\HarmonicaDesk.Setup
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=口琴小曲库
LicenseFile={#RepoDir}\LICENSE
SetupIconFile={#RepoDir}\src\Harmonica.Desktop\Assets\App.ico
OutputDir={#OutputDir}
OutputBaseFilename=HarmonicaDesk-Setup-{#AppVersion}-win-x64
VersionInfoVersion=0.3.0.1
VersionInfoProductName=口琴小曲库
VersionInfoProductVersion=0.3.0.1
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UsePreviousTasks=yes

[LangOptions]
LanguageName=简体中文
LanguageID=$0804

[Messages]
SetupAppTitle=安装
UninstallAppTitle=卸载
UninstallAppFullTitle=卸载 %1
ConfirmTitle=确认
ErrorTitle=错误
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonNo=否(&N)
ButtonFinish=完成(&F)
WelcomeLabel1=欢迎安装 [name]
WelcomeLabel2=即将安装 [name/ver]。%n%n软件只安装到当前用户目录，不需要安装开发环境。
ClickNext=点击“下一步”继续，或点击“取消”退出安装。
WizardLicense=使用许可
LicenseLabel=请阅读以下许可说明。
LicenseLabel3=继续安装前，请阅读并接受本项目的使用许可。
LicenseAccepted=我接受许可协议(&A)
LicenseNotAccepted=我不接受许可协议(&D)
WizardSelectTasks=快捷方式
SelectTasksDesc=选择需要创建的快捷方式。
SelectTasksLabel2=开始菜单入口会自动创建，也可以选择在桌面显示图标。
WizardReady=准备安装
ReadyLabel1=已准备好安装 [name]。
ReadyLabel2a=点击“安装”继续，或点击“上一步”检查设置。
ReadyLabel2b=点击“安装”继续。
ReadyMemoDir=安装位置：
ReadyMemoTasks=额外选项：
WizardPreparing=准备安装
PreparingDesc=正在准备安装 [name]。
WizardInstalling=正在安装
InstallingLabel=正在安装 [name]，请稍候。
FinishedHeadingLabel=安装完成
FinishedLabel=已安装 [name]。现在可以通过桌面或开始菜单快捷方式打开。
FinishedLabelNoIcons=已安装 [name]。
ClickFinish=点击“完成”关闭安装程序。
StatusCreateDirs=正在创建目录…
StatusExtractFiles=正在安装文件…
StatusCreateIcons=正在创建快捷方式…
StatusSavingUninstall=正在保存卸载信息…
StatusRunProgram=正在完成安装…
SetupAppRunningError=%1 正在运行。%n%n请先正常关闭软件，再点击“确定”继续安装。
UninstallAppRunningError=%1 正在运行。%n%n请先正常关闭软件，再点击“确定”继续卸载。

[Tasks]
Name: desktopicon; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
; This generated include contains one reviewed file per line, with no wildcards.
#include FilesInclude

[Icons]
Name: "{userprograms}\口琴小曲库"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\口琴小曲库"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "打开口琴小曲库"; Flags: nowait postinstall skipifsilent unchecked

; No Run key, scheduled task, or user-data deletion is installed. Inno removes
; only installed files; %LOCALAPPDATA%\HarmonicaDesk belongs to the user.
