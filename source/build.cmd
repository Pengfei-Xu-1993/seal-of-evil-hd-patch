@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat"
if errorlevel 1 exit /b %errorlevel%
cd /d "%~dp0"
cl /nologo /LD /MT /std:c++17 /utf-8 /W4 /O2 /EHsc dinput8.cpp /link /DEF:dinput8.def /OUT:dinput8.dll kernel32.lib user32.lib gdi32.lib
exit /b %errorlevel%
