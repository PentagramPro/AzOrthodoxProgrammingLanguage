@echo off
rem Трансляторъ языка «Азъ»:  az собери файлъ.азъ  |  az пусти файлъ.азъ  |  az си файлъ.азъ
chcp 65001 >nul
if not exist "%~dp0bin\az.exe" call "%~dp0собрать.cmd" >nul || exit /b 1
"%~dp0bin\az.exe" %*
