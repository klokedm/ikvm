# Semantika port .NET Core of Windward IKVM

## Build

- You need JDK 8.

- You need .NET Core SDK 3.1

- Download openjdk-8u45-b14 (http://www.frijters.net/openjdk-8u45-b14-stripped.zip) and unpack in the peer folder to ikvm.

- In the root folder run "dotnet build --configuration Release"

## Known Issues

This is work in progress. Currently the project builds, but ikvmc (IKVM Compiler) is unable to produce the IKVM.OpenJDK DLLs form the compiled java classes.
