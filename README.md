---
page_type: sample
languages:
- csharp
products:
- azure
name: Microsoft Information Protection File SDK .NET Core Sample App
description: "This sample application demonstrates using the Microsoft Information Protection SDK .NET wrapper to label and read a label from a file."
urlFragment: mip-filesdk-dotnet-core
---

# MIP SDK .NET Core Sample

This sample application will work on Ubuntu 18.04 or Windows. It's important to install the correct NuGet package, depending on which platform you're using.

## Ubuntu 18.04

### Install .NET Core

 [Install .NET Core SDK on Ubuntu](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu)

### Install the MIP SDK dependencies on Linux.

```bash
sudo apt-get install scons libgsf-1-dev libssl-dev libsecret-1-dev freeglut3-dev libcpprest-dev libcurl3-dev uuid-dev
```
### Install the Microsoft Authentication Library dependency

MSAL on Ubuntu, when authentication in a public client application, will use a browser to perform authentication. This requires the xdg-utils package. 

```bash
sudo apt-get install xdg-utils
```

### Install the NuGet Packages

The Ubuntu package is a separate package from the Windows C++/.NET Package. In the project directory, add the package by running:

```bash
dotnet add package Microsoft.Extensions.Configuration
dotnet add packageMicrosoft.Extensions.Configuration.FileExtensions
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package microsoft.identity.client
dotnet add package Microsoft.InformationProtection.File.Ubuntu1804
```

If you've cloned the project, the packages will restore upon first build. 

### Build the project and run

```bash
dotnet build --output ../bin/Debug
cd /bin/Debug/netcoreapp3.1
```

## Windows

