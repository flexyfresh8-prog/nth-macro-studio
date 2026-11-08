@echo off
rem NTH Macro Studio — one-command build wrapper.
rem Forwards all arguments to build.ps1 (e.g. build.cmd -SelfContained).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
