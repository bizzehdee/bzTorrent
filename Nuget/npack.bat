@echo off

mkdir lib
mkdir lib\net40

copy ..\bin\Release\*.dll lib\net40\

nuget pack System.Net.Torrent.nuspec
