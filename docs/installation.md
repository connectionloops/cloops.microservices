# 🧩 Installation

## Pre-requisite: Run NATS server

1. Install docker from [here](https://www.docker.com/get-started/) if you don't already have it.
2. Install .NET SDK 9.0 from [here](https://dotnet.microsoft.com/en-us/download/dotnet) Make sure to download the correct version as per your OS and architecture. Make sure to download dotnet 9 not 10.

### Starting NATS server for local dev

```bash
docker pull nats:latest
docker run -p 4222:4222 -ti nats:latest --jetstream
```

### Installing NATS CLI

The NATS CLI is useful for local debugging and managing your NATS server. While the CLI automatically connects to `nats://127.0.0.1:4222` by default, setting up a context is recommended for clarity.

#### macOS

```bash
brew tap nats-io/nats-tools
brew install nats-io/nats-tools/nats
```

#### Windows

**Option 1: Using Scoop (if you have Scoop installed)**

```powershell
scoop bucket add extras
scoop install extras/natscli
```

**Option 2: Manual Installation**

1. Download the latest NATS CLI release from the [official NATS CLI releases page](https://github.com/nats-io/natscli/releases)
2. Extract the `nats-<version>-windows-amd64.zip` file
3. Add the directory containing `nats.exe` to your system PATH:
   - Press `Win + R`, type `sysdm.cpl`, and press Enter
   - Go to the "Advanced" tab and click "Environment Variables"
   - Under "System variables," select "Path" and click "Edit"
   - Click "New" and add the path to the directory containing `nats.exe`
   - Click "OK" to save changes
4. Verify installation by opening a new command prompt and running:
   ```powershell
   nats --version
   ```

#### Setting up a local context (Optional but recommended)

After installing the NATS CLI, you can create a context pointing to your local NATS instance:

```bash
# Create a context named 'local' pointing to the local NATS server
nats context save local --server=nats://127.0.0.1:4222

# Set 'local' as the default context
nats context select local
```

> **Note:** The NATS CLI will automatically connect to `nats://127.0.0.1:4222` by default, so setting up a context is optional. However, it's useful for managing multiple environments or when you need to switch between different NATS servers.

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
