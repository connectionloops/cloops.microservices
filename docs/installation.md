# 🧩 Installation

## Pre-requisite: Run NATS server

1. Install docker from [here](https://www.docker.com/get-started/) if you don't already have it.
2. Install .NET SDK 9.0 from [here](https://dotnet.microsoft.com/en-us/download/dotnet) Make sure to download the correct version as per your OS and architecture. Make sure to download dotnet 9 not 10.

### Starting NATS server for local dev

```bash
docker pull nats:latest
docker run -p 4222:4222 -ti nats:latest --jetstream
```

## ✨ New Projects (Recommended)

### GitHub Template

Easiest way to start with cloops.microservices especially if you are outside Connection Loops. Bootstrap from our [GitHub Template](https://github.com/connectionloops/cloops.microservices.template)

### Nuget Template

This is the recommended way to setup a new project within Connection Loops as we do not use GitHub for our private repos.

```bash
# install the template (If you do not have it)
dotnet new install cloops.microservice.template

# If you have the template, but want to make sure you get the latest one
dotnet new uninstall cloops.microservice.template

# Create a new project
dotnet new cloops.microservice -n <name_of_service>
```

### 🔄 Existing Projects

Add cloops.nats package reference to your `.csproj` file. Please note we deliberately want all of our consumers to stay on latest version of the SDK.

```xml
<PackageReference Include="cloops.microservices" Version="*" />
```

Once added, just to `dotnet restore` to pull in the SDK.

Usage (Program.cs) -

```cs

using CLOOPS.microservices;

var app = new App();
await app.RunAsync();

```

### Custom Intro Message

You can provide a custom function to generate a personalized intro message when the application starts. The function receives `BaseAppSettings` as a parameter and should return a string.

```cs

using CLOOPS.microservices;

var app = new App((settings) =>
{
    return $@"
        ╔══════════════════════════════════════╗
        ║   My Custom Application Banner       ║
        ╚══════════════════════════════════════╝

        Application: {settings.AssemblyName}
        Environment: {settings.Cluster}
        NATS:        {settings.NatsURL}
    ";
});

await app.RunAsync();

```

If no custom function is provided, the default CLOOPS banner will be displayed.

---

> If you would like to know more about services. Please read [services](/docs/services.md)

## Next Steps

- **[Automated DI setup](./di.md)**
- [Back to documentation index](./README.md)
