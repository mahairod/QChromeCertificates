@echo off
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C# compiler not found: %CSC%
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /win32manifest:app.manifest /win32icon:app.ico /out:ChromeCertificatePolicyManager.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ChromeCertificatePolicyManager.cs
exit /b %ERRORLEVEL%
