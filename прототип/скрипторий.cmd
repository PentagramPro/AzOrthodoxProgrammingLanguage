@echo off
rem Скрипторїй — среда разработки для языка «Азъ». Можно передать путь къ проекту.
chcp 65001 >nul
title Скрипторий
python "%~dp0ide\server.py" %*
