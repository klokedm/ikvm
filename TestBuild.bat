cd ikvmc
dotnet build --configuration Release -p:TargetFramework=netcoreapp3.1
dotnet publish --configuration Release -p:TargetFramework=netcoreapp3.1
dotnet publish --configuration Release -p:TargetFramework=netstandard2.1
cd ..
dotnet build --configuration Release
copy bin\Release\netstandard2.1\IKVM.Runtime.dll bin\Release\netcoreapp3.1\
copy bin\Release\netstandard2.1\IKVM.Runtime.JNI.dll bin\Release\netcoreapp3.1\