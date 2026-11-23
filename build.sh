#!/bin/bash
# Builds Fizz.exe against the real .NET Framework 4.8 reference assemblies (runs on stock Windows 10/11).
set -e
cd "$(dirname "$0")"
API=/usr/lib/mono/4.8-api
LIB=lib/lhm096
mkdir -p out
python3 tools/make_res.py src/app.manifest src/fizz.ico out/fizz.res 1.2.0.0
mcs -target:winexe -platform:x64 -optimize+ -debug- -nostdlib -noconfig -langversion:7.2 \
  -out:out/Fizz.exe -win32res:out/fizz.res -resource:src/fizz.ico,Fizz.fizz.ico -resource:src/fizz-tray.ico,Fizz.fizz-tray.ico \
  -r:$API/mscorlib.dll -r:$API/System.dll -r:$API/System.Core.dll -r:$API/System.Drawing.dll -r:$API/System.Windows.Forms.dll \
  -r:$API/System.Runtime.Serialization.dll -r:$API/System.Xml.dll -r:$API/Facades/System.Runtime.dll \
  -r:$LIB/LibreHardwareMonitorLib.dll \
  src/*.cs
cp src/Fizz.exe.config out/
echo "built out/Fizz.exe ($(stat -c %s out/Fizz.exe) bytes)"
