@echo off
dotnet "%~dp0MigrateDeviceProcessingState.dll"
exit /b %errorlevel%
