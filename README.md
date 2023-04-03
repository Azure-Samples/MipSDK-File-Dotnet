---
page_type: sample
languages:
- csharp
products:
- m365
- office-365
description: "This sample application demonstrates using the Microsoft Information Protection SDK .NET wrapper to label and read a label from a file."
urlFragment: MipSDK-File-Dotnet
---

# MIP SDK .NET 6.0 Sample

This Microsoft Information Protection File SDK sample can run on Windows, or Ubuntu 18.04/20.04. It demonstrates initializing the MIP SDK and labeling a file. 

.NET 6.0 or later is required to run the sample: https://dotnet.microsoft.com/en-us/download/dotnet/6.0

## Ubuntu

If you're running on Ubuntu, follow these steps to install the necessary dependencies. 

### Install the MIP SDK dependencies on Ubuntu

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

## Required NuGet Packages

In the project directory, add the required packages by running:

```bash
dotnet add package Microsoft.Extensions.Configuration
dotnet add packageMicrosoft.Extensions.Configuration.FileExtensions
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package microsoft.identity.client
```

If running on Ubuntu 18.04, install the Ubuntu 18.04 package.

```bash
# Ubuntu 18.04
dotnet add package Microsoft.InformationProtection.File.Ubuntu1804
```

If running on Ubuntu 20.04, install the Ubuntu 20.04 package.

```bash
# Ubuntu 20.04
dotnet add package Microsoft.InformationProtection.File.Ubuntu2004
```

If running on Windows, install the base MIP SDK package.

```powershell
# Windows Only
dotnet add package Microsoft.InformationProtection.File
```

### Update appsettings.json

[Register an application in Azure Active Directory.](https://docs.microsoft.com/information-protection/develop/setup-configure-mip#register-a-client-application-with-azure-active-directory) Once complete, populate the **appsettings.json** file with details from the application registration: clientId and tenantId. Change **ida:IsMultiTenantApp** depending upon the type of application you've registered.

### Build the project and run

From the **/src** directory, run the following to build:

```bash
dotnet build --output ../bin/Debug
```

The application will output to **mip-filesdk-dotnet/bin/Debug/net6.0**. Run the app by executing **./mipsdk.exe** or **./mipsdk**
