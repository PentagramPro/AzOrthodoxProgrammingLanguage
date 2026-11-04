@echo off
rem Скрипторий — среда разработки для языка «Азъ». Можно передать путь къ проекту.
if not exist "%~dp0bin\Skriptorij.exe" call "%~dp0собрать.cmd" || exit /b 1
start "" "%~dp0bin\Skriptorij.exe" %*
