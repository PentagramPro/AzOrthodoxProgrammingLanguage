@echo off
rem Собираетъ транслятор az и среду Скрипторий въ папку bin\ (нуженъ .NET 10 SDK).
chcp 65001 >nul
dotnet publish "%~dp0src\az\az.csproj" -c Release -o "%~dp0bin" --nologo -v q || exit /b 1
dotnet publish "%~dp0src\Skriptorij\Skriptorij.csproj" -c Release -o "%~dp0bin" --nologo -v q || exit /b 1
echo Собрано въ %~dp0bin. Слава Богу!
