---
page_type: sample
languages:
- csharp
products:
- azure
description: "This sample application demonstrates using the Microsoft Information Protection SDK .NET wrapper to label and read a label from a file."
urlFragment: mip-filesdk-dotnet-core
---

# MIP SDK .NET Core Sample

This sample application will work on Ubuntu 18.04/20.04 or Windows. It's important to install the correct NuGet package, depending on which platform you're using.

## Ubuntu 18.04 and 20.04

### Install .NET Core

 [Install .NET Core SDK on Ubuntu](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu)

### Install the MIP SDK dependencies on Linux.

```bash
sudo apt-get install scons libgsf-1-dev libssl-dev libsecret-1-dev freeglut3-dev libcpprest-dev libcurl3-dev uuid-dev
```

### Install the Microsoft Authentication Library dependency

MSAL on Ubuntu, when authentication in a public client application, will use a browser to perform authentication. This requires the xdg-utils package. This is included only for demonstration and not required for other auth patterns. 

```bash
sudo apt-get install xdg-utils
```

## Windows

Running on Windows requires that the [Visual C++ Runtime redistributable](https://visualstudio.microsoft.com/downloads/#microsoft-visual-c-redistributable-for-visual-studio-2019) is installed. 

## Install the NuGet Packages

This step is required only for new projects. Cloning this repo will auto-restore dependencies.

In the project directory, add the required packages by running:

```bash
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.FileExtensions
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package microsoft.identity.client
```

If running on Ubuntu 18.04, install the Ubuntu 18.04 package. 

```bash
dotnet add package Microsoft.InformationProtection.File.Ubuntu1804
```

If running on Ubuntu 20.04, install the Ubuntu 20.04 package. 

```bash
dotnet add package Microsoft.InformationProtection.File.Ubuntu2004
```

If running on Windows, install the base MIP SDK package. 

```bash
dotnet add package Microsoft.InformationProtection.File
```

### Update appsettings.json

[Register an application in Azure Active Directory.](https://docs.microsoft.com/information-protection/develop/setup-configure-mip#register-a-client-application-with-azure-active-directory) Once complete, populate the **appsettings.json** file with details from the application registration: clientId, tenantId, and redirect URI. Change **ida:IsMultiTenantApp** depending upon the type of application you've registered. 

### Build the project and run

From the **/src** directory, run the following to build: 

```bash
dotnet build --output ../bin/Debug
```

The application will output to **mip-filesdk-dotnet-core/bin/Debug/**. Run the app by executing **./mipsdk.exe** or **./mipsdk**

