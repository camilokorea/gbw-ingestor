# eBird Data Ingestor

This project is an automated data ingestion service that consumes the eBird API to fetch recent bird observations and stores them in a SQL Server database. The process is designed to run periodically, maintaining an updated local database of bird sightings from different regions around the world.

---

> **A Note on Creation**
> 
> This project, including its architecture, code structure, business logic, bug fixes, and documentation, was **100% generated with Artificial Intelligence using Gemini CLI 2.5 PRO**. The development was an iterative process of dialogue, debugging, and refactoring guided by a human and executed by the AI.

---

## Architecture

The project is built following the principles of **Clean Architecture** to ensure a clear separation of concerns, high cohesion, low coupling, and maintainability.

The solution is divided into the following layers:

### 1. Domain

Contains the core business entities and purest domain logic. It has no dependencies on any other layer.

- **Entities**: `Observation`, `Species`, `Location`, `State`, `Country`.

### 2. Application

Contains the application logic and use cases. It orchestrates the domain entities to perform tasks. It defines the abstractions (interfaces) for services that depend on external infrastructure.

- **Services**: `IngestionService`.
- **Contracts/Interfaces**: `IApplicationDbContext`, `IEbirdApiClient`, `IIngestionService`.
- **DTOs**: Data Transfer Objects for communicating with the external API.

### 3. Infrastructure

Implements the services defined in the `Application` layer. This is where all technical details and dependencies on external services (like the database or API clients) reside.

- **Persistence**: `EbirdIngestorDbContext` (Entity Framework Core implementation).
- **External Services**: `EbirdApiClient` (client implementation for the eBird API).

### 4. Worker

This is the application's entry point. It is a .NET Console project that composes the different layers through Dependency Injection and executes the ingestion process.

## Tech Stack

- **Framework**: .NET 9
- **ORM**: Entity Framework Core (Code-First)
- **Database**: SQL Server
- **External API**: eBird API v2

## Getting Started

Follow these steps to set up and run the project in your local environment.

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Access to a SQL Server instance
- An [eBird API Key](https://documenter.getpostman.com/view/664385/S1ENwy59)

### Configuration

1.  **Clone the repository.**
2.  **Create your local configuration file:**
    -   Navigate to `src/Worker/`.
    -   Copy the `appsettings.template.json` file and rename it to `appsettings.json`.
3.  **Edit `appsettings.json`:**
    -   Update the `DefaultConnection` connection string with your SQL Server details.
    -   Add your eBird API key to the `ApiKey` field.

### Running the Application

1.  **Apply database migrations:**
    -   Open a terminal at the project root.
    -   Run the following command to have Entity Framework create the database schema:
        ```sh
        dotnet ef database update --project src/Infrastructure --startup-project src/Worker
        ```

2.  **Run the application:**
    -   In the same terminal, run the following command:
        ```sh
        dotnet run --project src/Worker
        ```
    -   The program will begin fetching and saving data for the regions defined in `Program.cs`.

## Deployment

This project uses a GitHub Actions workflow for continuous integration and deployment. The process is automatically triggered on every push to the `main` branch.

The workflow is defined in `.github/workflows/deploy.yml` and consists of two main jobs:

### 1. Build Job

This job is responsible for building and preparing the application for deployment.

- **Checkout & Setup**: Checks out the source code and sets up the specified .NET SDK.
- **Build & Publish**: Restores dependencies, builds the solution, and publishes the worker project in `Release` mode.
- **Configure**: Renames `appsettings.template.json` to `appsettings.json` and injects secrets (database credentials, API keys) into the configuration file.
- **Archive**: Uploads the published application as a build artifact, which will be used by the deploy job.

### 2. Deploy Job

This job runs after the build job succeeds and is responsible for deploying the application to a Linode server.

- **Download Artifact**: Downloads the application artifact created in the build job.
- **Copy Files**: Uses `scp` to securely copy the application files to the target directory (`/APPS/ebird-ingestor`) on the Linode server.
- **Restart Service**: Uses `ssh` to connect to the server and execute a script that:
    - Sets the correct file ownership and permissions.
    - Restarts and enables the `ebird-ingestor.service` systemd service, ensuring the new version of the application is running.
