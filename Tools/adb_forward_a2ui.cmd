@echo off
adb forward tcp:18766 tcp:18766
adb forward --list
echo A2UI bench forward ready: PC 127.0.0.1:18766 -^> device 127.0.0.1:18766
