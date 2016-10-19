@echo off 
echo Compiling..
"C:\git\corert\tests\\..\bin\Product\Windows_NT.x64.Debug\packaging\publish1\CoreRun.exe" "C:\git\corert\tests\\..\bin\Product\Windows_NT.x64.Debug\packaging\publish1\ilc.exe" @"c:\git\corert\xunit\ilc.rsp"

echo Linking..
link @"c:\git\corert\xunit\link.rsp"

echo Build complete