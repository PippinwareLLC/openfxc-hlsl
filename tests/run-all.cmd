@echo off
setlocal

dotnet test tests/OpenFXC.Hlsl.Tests/OpenFXC.Hlsl.Tests.csproj
set exitcode=%ERRORLEVEL%

endlocal & exit /b %exitcode%
