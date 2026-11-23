@echo off
setlocal
set "exporter=%~dp0tools\export_area_hints.py"
py -3 -c "import sys; sys.exit(sys.version_info < (3, 8))" >nul 2>&1
if errorlevel 1 goto try_python
py -3 -u "%exporter%" %*
goto done

:try_python
python -c "import sys; sys.exit(sys.version_info < (3, 8))" >nul 2>&1
if errorlevel 1 goto missing
python -u "%exporter%" %*
goto done

:missing
echo Python 3.8 or newer is required. Install Python from https://www.python.org/downloads/windows/
set "result=1"
goto finish

:done
set "result=%errorlevel%"
:finish
if "%~1"=="" pause
exit /b %result%
