@echo off
REM Make a printable QR code for the current network (output: qr_print folder)
python "%~dp0tools\make_qr.py" %*
pause
