cd ikvmc
dotnet build --configuration Release -p:TargetFramework=netcoreapp3.1
dotnet publish --configuration Release -p:TargetFramework=netcoreapp3.1
dotnet publish --configuration Release -p:TargetFramework=netstandard2.1
cd ..
dotnet build --configuration Release