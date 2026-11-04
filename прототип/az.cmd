@echo off
rem Трансляторъ языка «Азъ»:  az собери файлъ.азъ  |  az пусти файлъ.азъ  |  az си файлъ.азъ
chcp 65001 >nul
python "%~dp0compiler\az.py" %*
