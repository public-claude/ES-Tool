@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ============================================
echo   ES Tool  编译 + 部署
echo ============================================
echo.

if not exist "ModPcGUI\pcl\PCL.Core.dll" (
    echo [警告] ModPcGUI\pcl\PCL.Core.dll 不存在。
    echo        请先按 使用说明.md 第 1 步把 PCL2 的 exe 改名为 PCL.Core.dll 放进 pcl\
    echo        缺它编译能过，但运行起来没有 PCL 原生界面。
    echo.
)

set SRC=ModPcGUI\src
set OUT=%SRC%\bin\Debug\net48

echo [1/3] 编译 %SRC% ...
pushd "%SRC%"
dotnet build -v m
if errorlevel 1 (
    popd
    echo.
    echo [失败] 编译不通过，已中止部署。
    goto :end
)
popd

echo.
echo [2/3] 检查编译产物 ...
if not exist "%OUT%\ModPcGUI.exe" (
    echo [失败] 没找到 %OUT%\ModPcGUI.exe
    goto :end
)
echo       %OUT%\ModPcGUI.exe

REM ---- 部署目标：优先同级的 ES_Tool_data（运行时数据所在目录）----
set DEST=
if exist "..\ES_Tool_data\config.json" set DEST=..\ES_Tool_data
if "!DEST!"=="" if exist "ES_Tool_data\config.json" set DEST=ES_Tool_data

echo.
echo [3/3] 部署 ...
if "!DEST!"=="" (
    echo       [跳过] 没找到 ES_Tool_data，只保留编译产物。
    echo              若要单独运行，把 %OUT% 整个目录复制走即可
    echo              （程序首次启动会自动补 config.json 和 user\）。
    goto :end
)

echo       目标：!DEST!
tasklist /FI "IMAGENAME eq ModPcGUI.exe" 2>nul | find /I "ModPcGUI.exe" >nul
if not errorlevel 1 (
    echo.
    echo [警告] 检测到 ModPcGUI.exe 正在运行，请先关闭它，否则部署会失败。
    echo.
    pause
)

if exist "!DEST!\ModPcGUI.exe" copy /y "!DEST!\ModPcGUI.exe" "!DEST!\ModPcGUI.exe.bak" >nul
copy /y "%OUT%\ModPcGUI.exe" "!DEST!\ModPcGUI.exe" >nul
if errorlevel 1 (
    echo [失败] 复制 exe 失败（文件被占用？关掉程序再试）。
    goto :end
)

REM 样式表按需同步：改了 PclStyles.xaml 才需要
if exist "%OUT%\pcl\PclStyles.xaml" (
    copy /y "%OUT%\pcl\PclStyles.xaml" "!DEST!\pcl\PclStyles.xaml" >nul
)
if exist "!DEST!\crash.log" move /y "!DEST!\crash.log" "!DEST!\crash.log.prev" >nul

echo       已部署：!DEST!\ModPcGUI.exe
echo.
echo [完成] 双击 !DEST!\ModPcGUI.exe 运行。

:end
echo.
pause
