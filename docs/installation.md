# 🧩 Installation

## ✨ New Projects (Recommended)

### GitHub Template

Easiest way to start with cloops.microservices. Bootstrap from our [GitHub Template](https://github.com/connectionloops/cloops.microservices.template)

### Nuget Template

```bash
# install the template (If you do not have it)
dotnet new install cloops.microservice.template

# If you have the template, but want to make sure you get the latest one
dotnet new uninstall cloops.microservices.template

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

- **[How to register NATS consumer]**
