@echo off
setlocal
cd /d "%~dp0"
title Not Defteri - derleniyor

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo .NET Framework 4 derleyicisi bulunamadi: %FW%\csc.exe
  pause
  exit /b 1
)
set "WPF=%FW%\WPF"

rem Acik bir surum varsa kapat (notlar zaten otomatik kaydediliyor)
taskkill /im NotDefteri.exe /f >nul 2>&1
timeout /t 1 /nobreak >nul

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:NotDefteri.exe /win32icon:app.ico ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Core.dll /r:System.Xml.dll ^
  kaynak\*.cs > derleme-sonucu.txt 2>&1

if errorlevel 1 (
  type derleme-sonucu.txt
  echo.
  echo ============================================================
  echo  DERLEME BASARISIZ.
  echo  "derleme-sonucu.txt" dosyasini Claude'a gonder, duzeltsin.
  echo ============================================================
  pause
  exit /b 1
)

echo Derleme tamam. Not Defteri baslatiliyor...
start "" "%~dp0NotDefteri.exe"
timeout /t 2 /nobreak >nul
